#define NOMINMAX

#include <windows.h>
#include <windowsx.h>
#include <d2d1.h>
#include <dwrite.h>
#include <wincodec.h>
#include <wincrypt.h>
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cwctype>
#include <iostream>
#include <mutex>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "d2d1.lib")
#pragma comment(lib, "dwrite.lib")
#pragma comment(lib, "windowscodecs.lib")
#pragma comment(lib, "crypt32.lib")
#pragma comment(lib, "ole32.lib")

namespace
{
    constexpr wchar_t kWindowClass[] = L"YUCPCompanionTutorialOverlay";
    constexpr UINT kFrameMessage = WM_APP + 1;
    constexpr BYTE kAlphaFormat = 1;
    constexpr DWORD kLayeredAlpha = 0x00000002;
    constexpr int kCardWidth = 380;
    constexpr int kCardHeight = 182;
    constexpr double kTransitionSeconds = 0.44;

    template <typename T>
    void SafeRelease(T*& value)
    {
        if (value)
        {
            value->Release();
            value = nullptr;
        }
    }

    struct RectI
    {
        int x = 0;
        int y = 0;
        int w = 0;
        int h = 0;

        bool Contains(int px, int py) const
        {
            return px >= x && py >= y && px < x + w && py < y + h;
        }
    };

    struct FloatRect
    {
        float x = 0.0f;
        float y = 0.0f;
        float w = 0.0f;
        float h = 0.0f;
    };

    struct AnimatedOverlayState
    {
        bool initialized = false;
        std::wstring key;
        FloatRect spotlightFrom;
        FloatRect spotlightTo;
        FloatRect spotlightCurrent;
        FloatRect cardFrom;
        FloatRect cardTo;
        FloatRect cardCurrent;
        float cursorFromX = 0.0f;
        float cursorFromY = 0.0f;
        float cursorToX = 0.0f;
        float cursorToY = 0.0f;
        float cursorCurrentX = 0.0f;
        float cursorCurrentY = 0.0f;
        std::chrono::steady_clock::time_point transitionStartedAt;
    };

    struct FrameState
    {
        DWORD parentPid = 0;
        int x = 0;
        int y = 0;
        int width = 1;
        int height = 1;
        bool targetResolved = false;
        RectI target;
        int padLeft = 12;
        int padTop = 12;
        int padRight = 12;
        int padBottom = 12;
        bool canBack = false;
        bool isLast = false;
        std::wstring tutorialTitle;
        std::wstring stepTitle;
        std::wstring stepText;
        std::wstring counter;
        std::wstring wait;
        std::wstring mouseAction;
        std::wstring overlayMode;

        std::wstring Key() const
        {
            return counter + L"\n" + stepTitle + L"\n" + stepText + L"\n" + mouseAction + L"\n" + overlayMode;
        }
    };

    HWND g_hwnd = nullptr;
    ID2D1Factory* g_d2dFactory = nullptr;
    IDWriteFactory* g_dwriteFactory = nullptr;
    IWICImagingFactory* g_wicFactory = nullptr;
    IWICFormatConverter* g_cursorBitmapSource = nullptr;
    IDWriteTextFormat* g_titleFormat = nullptr;
    IDWriteTextFormat* g_bodyFormat = nullptr;
    IDWriteTextFormat* g_smallFormat = nullptr;
    std::mutex g_frameMutex;
    FrameState g_pendingFrame;
    FrameState g_frame;
    bool g_hasFrame = false;
    std::wstring g_lastFrameKey;
    std::chrono::steady_clock::time_point g_stepStartedAt;
    HANDLE g_parentProcess = nullptr;
    std::atomic<bool> g_running{true};
    RectI g_cardRect;
    RectI g_backButtonRect;
    RectI g_nextButtonRect;
    RectI g_closeButtonRect;
    AnimatedOverlayState g_animation;
    bool g_clickThrough = false;

    bool IsPopupHit(int x, int y)
    {
        if (!g_hasFrame)
            return false;

        if (g_cardRect.w <= 1 || g_cardRect.h <= 1)
            return false;

        if (x < 0 || y < 0 || x >= g_frame.width || y >= g_frame.height)
            return false;

        return g_cardRect.Contains(x, y);
    }

    bool IsCursorOverPopup()
    {
        if (!g_hwnd || !g_hasFrame)
            return false;

        POINT cursor{};
        if (!GetCursorPos(&cursor))
            return false;

        return IsPopupHit(cursor.x - g_frame.x, cursor.y - g_frame.y);
    }

    void SetClickThrough(bool enabled)
    {
        if (!g_hwnd || g_clickThrough == enabled)
            return;

        LONG_PTR style = GetWindowLongPtr(g_hwnd, GWL_EXSTYLE);
        if (enabled)
            style |= WS_EX_TRANSPARENT;
        else
            style &= ~static_cast<LONG_PTR>(WS_EX_TRANSPARENT);

        SetWindowLongPtr(g_hwnd, GWL_EXSTYLE, style);
        SetWindowPos(g_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        g_clickThrough = enabled;
    }

    void UpdateClickThrough()
    {
        SetClickThrough(!IsCursorOverPopup());
    }

    std::vector<std::string> SplitTabs(const std::string& line)
    {
        std::vector<std::string> parts;
        std::string part;
        std::stringstream stream(line);
        while (std::getline(stream, part, '\t'))
            parts.push_back(part);
        return parts;
    }

    int ToInt(const std::string& value)
    {
        try
        {
            return std::stoi(value);
        }
        catch (...)
        {
            return 0;
        }
    }

    std::wstring Utf8ToWide(const std::string& value)
    {
        if (value.empty())
            return L"";

        int size = MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0);
        std::wstring result(size, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), result.data(), size);
        return result;
    }

    std::wstring DecodeText(const std::string& value)
    {
        if (value.empty())
            return L"";

        DWORD outputSize = 0;
        if (!CryptStringToBinaryA(value.c_str(), static_cast<DWORD>(value.size()), CRYPT_STRING_BASE64, nullptr, &outputSize, nullptr, nullptr))
            return Utf8ToWide(value);

        std::string decoded(outputSize, '\0');
        if (!CryptStringToBinaryA(value.c_str(), static_cast<DWORD>(value.size()), CRYPT_STRING_BASE64,
            reinterpret_cast<BYTE*>(decoded.data()), &outputSize, nullptr, nullptr))
        {
            return Utf8ToWide(value);
        }

        decoded.resize(outputSize);
        return Utf8ToWide(decoded);
    }

    bool ParseFrame(const std::string& line, FrameState& frame)
    {
        std::vector<std::string> parts = SplitTabs(line);
        if (parts.size() < 22 || parts[0] != "FRAME")
            return false;

        frame.parentPid = static_cast<DWORD>(ToInt(parts[1]));
        frame.x = ToInt(parts[2]);
        frame.y = ToInt(parts[3]);
        frame.width = std::max(1, ToInt(parts[4]));
        frame.height = std::max(1, ToInt(parts[5]));
        frame.targetResolved = parts[6] == "1";
        frame.target.x = ToInt(parts[7]);
        frame.target.y = ToInt(parts[8]);
        frame.target.w = ToInt(parts[9]);
        frame.target.h = ToInt(parts[10]);
        frame.padLeft = ToInt(parts[11]);
        frame.padTop = ToInt(parts[12]);
        frame.padRight = ToInt(parts[13]);
        frame.padBottom = ToInt(parts[14]);
        frame.canBack = parts[15] == "1";
        frame.isLast = parts[16] == "1";
        frame.tutorialTitle = DecodeText(parts[17]);
        frame.stepTitle = DecodeText(parts[18]);
        frame.stepText = DecodeText(parts[19]);
        frame.counter = DecodeText(parts[20]);
        frame.wait = DecodeText(parts[21]);
        frame.mouseAction = parts.size() >= 23 ? DecodeText(parts[22]) : L"none";
        frame.overlayMode = parts.size() >= 24 ? DecodeText(parts[23]) : L"intrusive";
        return true;
    }

    D2D1_RECT_F RectF(float x, float y, float w, float h)
    {
        return D2D1::RectF(x, y, x + w, y + h);
    }

    D2D1_RECT_F RectF(const RectI& rect)
    {
        return RectF(static_cast<float>(rect.x), static_cast<float>(rect.y), static_cast<float>(rect.w), static_cast<float>(rect.h));
    }

    D2D1_RECT_F RectF(const FloatRect& rect)
    {
        return RectF(rect.x, rect.y, rect.w, rect.h);
    }

    D2D1_ROUNDED_RECT RoundedRect(const RectI& rect, float radius)
    {
        D2D1_ROUNDED_RECT rounded{};
        rounded.rect = RectF(rect);
        rounded.radiusX = radius;
        rounded.radiusY = radius;
        return rounded;
    }

    D2D1_ROUNDED_RECT RoundedRect(const FloatRect& rect, float radius)
    {
        D2D1_ROUNDED_RECT rounded{};
        rounded.rect = RectF(rect);
        rounded.radiusX = radius;
        rounded.radiusY = radius;
        return rounded;
    }

    FloatRect ToFloatRect(const RectI& rect)
    {
        return
        {
            static_cast<float>(rect.x),
            static_cast<float>(rect.y),
            static_cast<float>(rect.w),
            static_cast<float>(rect.h)
        };
    }

    RectI ToRectI(const FloatRect& rect)
    {
        return
        {
            static_cast<int>(std::round(rect.x)),
            static_cast<int>(std::round(rect.y)),
            std::max(1, static_cast<int>(std::round(rect.w))),
            std::max(1, static_cast<int>(std::round(rect.h)))
        };
    }

    float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    FloatRect LerpRect(const FloatRect& a, const FloatRect& b, float t)
    {
        return
        {
            Lerp(a.x, b.x, t),
            Lerp(a.y, b.y, t),
            Lerp(a.w, b.w, t),
            Lerp(a.h, b.h, t)
        };
    }

    float SmootherStep(float t)
    {
        t = std::clamp(t, 0.0f, 1.0f);
        return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
    }

    float EaseOutCubic(float t)
    {
        t = std::clamp(t, 0.0f, 1.0f);
        float inv = 1.0f - t;
        return 1.0f - inv * inv * inv;
    }

    bool RectChanged(const FloatRect& a, const FloatRect& b)
    {
        return std::fabs(a.x - b.x) > 0.75f ||
            std::fabs(a.y - b.y) > 0.75f ||
            std::fabs(a.w - b.w) > 0.75f ||
            std::fabs(a.h - b.h) > 0.75f;
    }

    std::wstring Lowercase(std::wstring value)
    {
        std::transform(value.begin(), value.end(), value.begin(),
            [](wchar_t c) { return static_cast<wchar_t>(std::towlower(c)); });
        return value;
    }

    bool IsUnintrusiveMode(const FrameState& frame)
    {
        std::wstring mode = Lowercase(frame.overlayMode);
        return mode == L"unintrusive" || mode == L"nonintrusive" || mode == L"minimal" || mode == L"cursor";
    }

    RectI ClampSpotlight(const FrameState& frame)
    {
        if (!frame.targetResolved)
            return { frame.width / 2 - 120, frame.height / 2 - 70, 240, 140 };

        int left = std::clamp(frame.target.x - frame.padLeft, 0, std::max(0, frame.width - 1));
        int top = std::clamp(frame.target.y - frame.padTop, 0, std::max(0, frame.height - 1));
        int right = std::clamp(frame.target.x + frame.target.w + frame.padRight, left + 1, frame.width);
        int bottom = std::clamp(frame.target.y + frame.target.h + frame.padBottom, top + 1, frame.height);
        return { left, top, right - left, bottom - top };
    }

    void DrawTextBlock(ID2D1RenderTarget* target, IDWriteTextFormat* format, ID2D1Brush* brush,
        const std::wstring& text, const D2D1_RECT_F& rect)
    {
        target->DrawTextW(text.c_str(), static_cast<UINT32>(text.size()), format, rect, brush,
            D2D1_DRAW_TEXT_OPTIONS_CLIP);
    }

    void FillRounded(ID2D1RenderTarget* target, ID2D1Brush* brush, const RectI& rect, float radius)
    {
        D2D1_ROUNDED_RECT rounded = RoundedRect(rect, radius);
        target->FillRoundedRectangle(rounded, brush);
    }

    void DrawButton(ID2D1RenderTarget* target, const RectI& rect, const std::wstring& text, bool primary, bool enabled)
    {
        ID2D1SolidColorBrush* fill = nullptr;
        ID2D1SolidColorBrush* stroke = nullptr;
        ID2D1SolidColorBrush* label = nullptr;

        D2D1_COLOR_F fillColor = enabled
            ? (primary ? D2D1::ColorF(0.211f, 0.749f, 0.694f, 0.20f) : D2D1::ColorF(1.0f, 1.0f, 1.0f, 0.05f))
            : D2D1::ColorF(1.0f, 1.0f, 1.0f, 0.035f);
        D2D1_COLOR_F strokeColor = enabled
            ? (primary ? D2D1::ColorF(0.211f, 0.749f, 0.694f, 0.50f) : D2D1::ColorF(1.0f, 1.0f, 1.0f, 0.10f))
            : D2D1::ColorF(1.0f, 1.0f, 1.0f, 0.06f);
        target->CreateSolidColorBrush(fillColor, &fill);
        target->CreateSolidColorBrush(strokeColor, &stroke);
        target->CreateSolidColorBrush(enabled
            ? (primary ? D2D1::ColorF(0.211f, 0.749f, 0.694f, 1.0f) : D2D1::ColorF(0.878f, 0.878f, 0.878f, 1.0f))
            : D2D1::ColorF(0.46f, 0.46f, 0.46f, 1.0f), &label);

        D2D1_ROUNDED_RECT rounded = RoundedRect(rect, 6.0f);
        target->FillRoundedRectangle(rounded, fill);
        target->DrawRoundedRectangle(rounded, stroke, 1.0f);

        g_smallFormat->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
        g_smallFormat->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        DrawTextBlock(target, g_smallFormat, label, text, RectF(rect));
        g_smallFormat->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_LEADING);
        g_smallFormat->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_NEAR);

        SafeRelease(label);
        SafeRelease(stroke);
        SafeRelease(fill);
    }

    bool FileExists(const std::wstring& path)
    {
        DWORD attributes = GetFileAttributesW(path.c_str());
        return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0;
    }

    std::wstring GetExecutableDirectory()
    {
        wchar_t path[MAX_PATH]{};
        DWORD length = GetModuleFileNameW(nullptr, path, MAX_PATH);
        if (length == 0 || length >= MAX_PATH)
            return L"";

        std::wstring result(path, length);
        size_t slash = result.find_last_of(L"\\/");
        if (slash == std::wstring::npos)
            return L"";

        return result.substr(0, slash);
    }

    std::wstring ResolveCursorAssetPath()
    {
        std::vector<std::wstring> candidates;
        candidates.push_back(L"Packages\\com.yucp.devtools\\Editor\\PackageExporter\\CompanionTutorial\\Assets\\Pointer.svg");

        std::wstring exeDir = GetExecutableDirectory();
        if (!exeDir.empty())
        {
            candidates.push_back(exeDir + L"\\..\\..\\CompanionTutorial\\Assets\\Pointer.svg");
            candidates.push_back(exeDir + L"\\Pointer.svg");
        }

        for (const std::wstring& candidate : candidates)
        {
            if (FileExists(candidate))
                return candidate;
        }

        return L"";
    }

    bool EnsureCursorBitmapLoaded()
    {
        if (g_cursorBitmapSource)
            return true;

        if (!g_wicFactory)
            return false;

        std::wstring path = ResolveCursorAssetPath();
        if (path.empty())
            return false;

        IWICBitmapDecoder* decoder = nullptr;
        IWICBitmapFrameDecode* frame = nullptr;
        IWICFormatConverter* converter = nullptr;

        HRESULT hr = g_wicFactory->CreateDecoderFromFilename(
            path.c_str(),
            nullptr,
            GENERIC_READ,
            WICDecodeMetadataCacheOnLoad,
            &decoder);

        if (SUCCEEDED(hr))
            hr = decoder->GetFrame(0, &frame);

        if (SUCCEEDED(hr))
            hr = g_wicFactory->CreateFormatConverter(&converter);

        if (SUCCEEDED(hr))
        {
            hr = converter->Initialize(
                frame,
                GUID_WICPixelFormat32bppPBGRA,
                WICBitmapDitherTypeNone,
                nullptr,
                0.0,
                WICBitmapPaletteTypeMedianCut);
        }

        if (SUCCEEDED(hr))
        {
            g_cursorBitmapSource = converter;
            converter = nullptr;
        }

        SafeRelease(converter);
        SafeRelease(frame);
        SafeRelease(decoder);
        return g_cursorBitmapSource != nullptr;
    }

    void DrawCursor(ID2D1RenderTarget* target, float tipX, float tipY)
    {
        if (EnsureCursorBitmapLoaded())
        {
            ID2D1Bitmap* bitmap = nullptr;
            if (SUCCEEDED(target->CreateBitmapFromWicBitmap(g_cursorBitmapSource, nullptr, &bitmap)))
            {
                constexpr float cursorSize = 34.0f;
                D2D1_RECT_F destination = D2D1::RectF(tipX - 2.0f, tipY - 2.0f, tipX - 2.0f + cursorSize, tipY - 2.0f + cursorSize);
                target->DrawBitmap(bitmap, destination, 1.0f, D2D1_BITMAP_INTERPOLATION_MODE_LINEAR);
                SafeRelease(bitmap);
                return;
            }
        }

        ID2D1PathGeometry* geometry = nullptr;
        ID2D1GeometrySink* sink = nullptr;
        ID2D1SolidColorBrush* shadow = nullptr;
        ID2D1SolidColorBrush* fill = nullptr;
        ID2D1SolidColorBrush* stroke = nullptr;

        g_d2dFactory->CreatePathGeometry(&geometry);
        geometry->Open(&sink);
        const float s = 0.22f;
        const float ox = tipX - 1.5f * s;
        const float oy = tipY - 1.5f * s;
        auto p = [&](float x, float y) { return D2D1::Point2F(ox + x * s, oy + y * s); };
        auto b = [&](float x1, float y1, float x2, float y2, float x3, float y3)
        {
            D2D1_BEZIER_SEGMENT segment = { p(x1, y1), p(x2, y2), p(x3, y3) };
            sink->AddBezier(segment);
        };

        sink->BeginFigure(p(124.111f, 22.7199f), D2D1_FIGURE_BEGIN_FILLED);
        b(95.3704f, 8.21595f, 62.0368f, 0.683627f, 26.0969f, 3.24449f);
        b(13.7632f, 4.12331f, 4.01617f, 13.9243f, 3.18141f, 26.2631f);
        b(0.743997f, 62.2908f, 8.40115f, 95.6722f, 23.0398f, 124.416f);
        b(31.4185f, 140.87f, 53.2901f, 141.847f, 64.7971f, 128.671f);
        b(68.0167f, 124.985f, 71.085f, 121.263f, 73.8987f, 117.402f);
        b(83.6319f, 127.329f, 93.9297f, 136.668f, 104.896f, 145.527f);
        b(113.046f, 152.112f, 125.21f, 153.748f, 134.319f, 146.754f);
        b(139.06f, 143.113f, 143.139f, 139.028f, 146.774f, 134.28f);
        b(153.754f, 125.166f, 152.123f, 112.997f, 145.551f, 104.838f);
        b(136.671f, 93.8154f, 127.307f, 83.4676f, 117.353f, 73.6886f);
        b(121.193f, 70.8464f, 124.894f, 67.7538f, 128.559f, 64.5127f);
        b(141.657f, 52.9293f, 140.592f, 31.0371f, 124.111f, 22.7199f);
        sink->EndFigure(D2D1_FIGURE_END_CLOSED);
        sink->Close();

        target->CreateSolidColorBrush(D2D1::ColorF(0, 0, 0, 0.35f), &shadow);
        target->CreateSolidColorBrush(D2D1::ColorF(1.0f, 1.0f, 1.0f, 1.0f), &fill);
        target->CreateSolidColorBrush(D2D1::ColorF(0.18f, 0.19f, 0.21f, 1.0f), &stroke);

        target->SetTransform(D2D1::Matrix3x2F::Translation(3.0f, 4.0f));
        target->FillGeometry(geometry, shadow);
        target->SetTransform(D2D1::Matrix3x2F::Identity());
        target->FillGeometry(geometry, fill);
        target->DrawGeometry(geometry, stroke, 1.15f);

        SafeRelease(stroke);
        SafeRelease(fill);
        SafeRelease(shadow);
        SafeRelease(sink);
        SafeRelease(geometry);
    }

    RectI ComputeCardRect(const FrameState& frame, const FloatRect& spotlight)
    {
        int cardWidth = std::min(kCardWidth, std::max(280, frame.width - 48));
        int margin = 24;
        int cardX = static_cast<int>(std::round(spotlight.x + spotlight.w + margin));
        if (cardX + cardWidth > frame.width - margin)
            cardX = static_cast<int>(std::round(spotlight.x - margin - cardWidth));
        if (cardX < margin)
            cardX = std::max(margin, (frame.width - cardWidth) / 2);

        int cardY = static_cast<int>(std::round(spotlight.y));
        if (cardY + kCardHeight > frame.height - margin)
            cardY = frame.height - margin - kCardHeight;
        if (cardY < margin)
            cardY = margin;

        return { cardX, cardY, cardWidth, kCardHeight };
    }

    void SetCardControls(const RectI& card)
    {
        g_cardRect = card;
        g_closeButtonRect = { card.x + card.w - 34, card.y + 13, 20, 20 };
        g_backButtonRect = { card.x + 18, card.y + card.h - 42, 84, 28 };
        g_nextButtonRect = { card.x + card.w - 102, card.y + card.h - 42, 84, 28 };
    }

    bool CopyWicBitmapToLayeredWindow(IWICBitmap* bitmap, int width, int height, int x, int y)
    {
        WICRect lockRect = { 0, 0, width, height };
        IWICBitmapLock* lock = nullptr;
        HRESULT hr = bitmap->Lock(&lockRect, WICBitmapLockRead, &lock);
        if (FAILED(hr))
            return false;

        UINT sourceStride = 0;
        UINT bufferSize = 0;
        BYTE* source = nullptr;
        lock->GetStride(&sourceStride);
        lock->GetDataPointer(&bufferSize, &source);

        HDC screenDc = GetDC(nullptr);
        HDC memDc = CreateCompatibleDC(screenDc);

        BITMAPINFO bitmapInfo{};
        bitmapInfo.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        bitmapInfo.bmiHeader.biWidth = width;
        bitmapInfo.bmiHeader.biHeight = -height;
        bitmapInfo.bmiHeader.biPlanes = 1;
        bitmapInfo.bmiHeader.biBitCount = 32;
        bitmapInfo.bmiHeader.biCompression = BI_RGB;

        void* bits = nullptr;
        HBITMAP dib = CreateDIBSection(screenDc, &bitmapInfo, DIB_RGB_COLORS, &bits, nullptr, 0);
        if (!dib || !bits)
        {
            SafeRelease(lock);
            DeleteDC(memDc);
            ReleaseDC(nullptr, screenDc);
            return false;
        }

        BYTE* destination = static_cast<BYTE*>(bits);
        const UINT destinationStride = static_cast<UINT>(width * 4);
        for (int row = 0; row < height; row++)
        {
            memcpy(destination + row * destinationStride, source + row * sourceStride, destinationStride);
        }

        HGDIOBJ oldBitmap = SelectObject(memDc, dib);
        SIZE size = { width, height };
        POINT src = { 0, 0 };
        POINT dst = { x, y };
        BLENDFUNCTION blend = { AC_SRC_OVER, 0, 255, kAlphaFormat };
        BOOL ok = UpdateLayeredWindow(g_hwnd, screenDc, &dst, &size, memDc, &src, 0, &blend, kLayeredAlpha);

        SelectObject(memDc, oldBitmap);
        DeleteObject(dib);
        DeleteDC(memDc);
        ReleaseDC(nullptr, screenDc);
        SafeRelease(lock);
        return ok == TRUE;
    }

    double SecondsSinceStepStart()
    {
        if (g_stepStartedAt.time_since_epoch().count() == 0)
            return 0.0;

        auto now = std::chrono::steady_clock::now();
        return std::chrono::duration<double>(now - g_stepStartedAt).count();
    }

    double SecondsSince(const std::chrono::steady_clock::time_point& time)
    {
        auto now = std::chrono::steady_clock::now();
        return std::chrono::duration<double>(now - time).count();
    }

    void RetargetAnimation(const std::wstring& key, const FloatRect& spotlight, const FloatRect& card, float cursorX, float cursorY)
    {
        auto now = std::chrono::steady_clock::now();
        bool newStep = g_animation.key != key;
        if (!g_animation.initialized)
        {
            g_animation.initialized = true;
            g_animation.key = key;
            g_animation.spotlightFrom = spotlight;
            g_animation.spotlightTo = spotlight;
            g_animation.spotlightCurrent = spotlight;
            g_animation.cardFrom = card;
            g_animation.cardTo = card;
            g_animation.cardCurrent = card;
            g_animation.cursorFromX = cursorX;
            g_animation.cursorFromY = cursorY;
            g_animation.cursorToX = cursorX;
            g_animation.cursorToY = cursorY;
            g_animation.cursorCurrentX = cursorX;
            g_animation.cursorCurrentY = cursorY;
            g_animation.transitionStartedAt = now;
            return;
        }

        if (!newStep)
        {
            bool transitionActive = SecondsSince(g_animation.transitionStartedAt) < kTransitionSeconds;
            if (transitionActive)
            {
                g_animation.spotlightTo = spotlight;
                g_animation.cardTo = card;
                g_animation.cursorToX = cursorX;
                g_animation.cursorToY = cursorY;
                return;
            }

            g_animation.spotlightFrom = spotlight;
            g_animation.spotlightTo = spotlight;
            g_animation.spotlightCurrent = spotlight;
            g_animation.cardFrom = card;
            g_animation.cardTo = card;
            g_animation.cardCurrent = card;
            g_animation.cursorFromX = cursorX;
            g_animation.cursorFromY = cursorY;
            g_animation.cursorToX = cursorX;
            g_animation.cursorToY = cursorY;
            g_animation.cursorCurrentX = cursorX;
            g_animation.cursorCurrentY = cursorY;
            return;
        }

        g_animation.key = key;
        g_animation.spotlightFrom = g_animation.spotlightCurrent;
        g_animation.spotlightTo = spotlight;
        g_animation.cardFrom = g_animation.cardCurrent;
        g_animation.cardTo = card;
        g_animation.cursorFromX = g_animation.cursorCurrentX;
        g_animation.cursorFromY = g_animation.cursorCurrentY;
        g_animation.cursorToX = cursorX;
        g_animation.cursorToY = cursorY;
        g_animation.transitionStartedAt = now;
    }

    void UpdateAnimation()
    {
        float t = static_cast<float>(SecondsSince(g_animation.transitionStartedAt) / kTransitionSeconds);
        t = SmootherStep(t);
        g_animation.spotlightCurrent = LerpRect(g_animation.spotlightFrom, g_animation.spotlightTo, t);
        g_animation.cardCurrent = LerpRect(g_animation.cardFrom, g_animation.cardTo, t);
        g_animation.cursorCurrentX = Lerp(g_animation.cursorFromX, g_animation.cursorToX, t);
        g_animation.cursorCurrentY = Lerp(g_animation.cursorFromY, g_animation.cursorToY, t);
    }

    void DrawPulse(ID2D1RenderTarget* target, float x, float y, float phase, const D2D1_COLOR_F& color)
    {
        if (phase < 0.0f || phase > 1.0f)
            return;

        float eased = EaseOutCubic(phase);
        float radius = Lerp(6.0f, 30.0f, eased);
        float alpha = color.a * (1.0f - phase);
        ID2D1SolidColorBrush* brush = nullptr;
        target->CreateSolidColorBrush(D2D1::ColorF(color.r, color.g, color.b, alpha), &brush);
        target->DrawEllipse(D2D1::Ellipse(D2D1::Point2F(x, y), radius, radius), brush, 2.2f);
        if (phase < 0.22f)
        {
            float dotAlpha = color.a * (1.0f - phase / 0.22f);
            SafeRelease(brush);
            target->CreateSolidColorBrush(D2D1::ColorF(color.r, color.g, color.b, dotAlpha), &brush);
            target->FillEllipse(D2D1::Ellipse(D2D1::Point2F(x, y), 5.0f, 5.0f), brush);
        }
        SafeRelease(brush);
    }

    void DrawDragCurve(ID2D1RenderTarget* target, float startX, float startY, float endX, float endY, float phase)
    {
        ID2D1PathGeometry* geometry = nullptr;
        ID2D1GeometrySink* sink = nullptr;
        ID2D1SolidColorBrush* trail = nullptr;
        ID2D1SolidColorBrush* dot = nullptr;

        float controlX = (startX + endX) * 0.5f - 24.0f;
        float controlY = std::min(startY, endY) - 46.0f;
        g_d2dFactory->CreatePathGeometry(&geometry);
        geometry->Open(&sink);
        sink->BeginFigure(D2D1::Point2F(startX, startY), D2D1_FIGURE_BEGIN_HOLLOW);
        D2D1_BEZIER_SEGMENT segment =
        {
            D2D1::Point2F(controlX, controlY),
            D2D1::Point2F(controlX + 38.0f, controlY),
            D2D1::Point2F(endX, endY)
        };
        sink->AddBezier(segment);
        sink->EndFigure(D2D1_FIGURE_END_OPEN);
        sink->Close();

        target->CreateSolidColorBrush(D2D1::ColorF(0.211f, 0.749f, 0.694f, 0.28f), &trail);
        target->DrawGeometry(geometry, trail, 3.0f);

        float t = EaseOutCubic(std::clamp(phase, 0.0f, 1.0f));
        float inv = 1.0f - t;
        float x = inv * inv * inv * startX + 3.0f * inv * inv * t * controlX + 3.0f * inv * t * t * (controlX + 38.0f) + t * t * t * endX;
        float y = inv * inv * inv * startY + 3.0f * inv * inv * t * controlY + 3.0f * inv * t * t * controlY + t * t * t * endY;

        target->CreateSolidColorBrush(D2D1::ColorF(0.211f, 0.749f, 0.694f, 0.90f), &dot);
        target->FillEllipse(D2D1::Ellipse(D2D1::Point2F(x, y), 5.5f, 5.5f), dot);

        SafeRelease(dot);
        SafeRelease(trail);
        SafeRelease(sink);
        SafeRelease(geometry);
    }

    void ApplyActionCursorMotion(const std::wstring& action, double elapsed, float& cursorX, float& cursorY)
    {
        std::wstring key = Lowercase(action);
        if (key == L"drag")
        {
            double loop = std::fmod(elapsed, 2.4);
            float phase = static_cast<float>(std::clamp((loop - 0.22) / 1.18, 0.0, 1.0));
            float eased = SmootherStep(phase);
            cursorX = Lerp(cursorX - 82.0f, cursorX, eased);
            cursorY = Lerp(cursorY + 58.0f, cursorY, eased);
            return;
        }

        double loop = std::fmod(elapsed, key == L"doubleclick" ? 1.9 : 1.45);
        bool down = loop > 0.12 && loop < 0.22;
        if (key == L"doubleclick")
            down = down || (loop > 0.34 && loop < 0.44);
        if (key == L"rightclick")
            down = loop > 0.18 && loop < 0.34;

        if (down)
        {
            cursorX += 1.0f;
            cursorY += 2.0f;
        }
    }

    void DrawMouseAction(ID2D1RenderTarget* target, const std::wstring& action, float cursorX, float cursorY)
    {
        std::wstring key = Lowercase(action);
        if (key.empty() || key == L"none")
            return;

        double elapsed = SecondsSinceStepStart();
        D2D1_COLOR_F teal = D2D1::ColorF(0.211f, 0.749f, 0.694f, 0.78f);
        D2D1_COLOR_F amber = D2D1::ColorF(0.95f, 0.72f, 0.32f, 0.76f);

        if (key == L"click")
        {
            float phase = static_cast<float>(std::fmod(elapsed, 1.45) / 0.72);
            DrawPulse(target, cursorX, cursorY, phase, teal);
            return;
        }

        if (key == L"doubleclick")
        {
            double loop = std::fmod(elapsed, 1.9);
            DrawPulse(target, cursorX, cursorY, static_cast<float>((loop - 0.08) / 0.55), teal);
            DrawPulse(target, cursorX, cursorY, static_cast<float>((loop - 0.32) / 0.55), teal);
            return;
        }

        if (key == L"rightclick")
        {
            float phase = static_cast<float>(std::fmod(elapsed, 1.65) / 0.76);
            DrawPulse(target, cursorX, cursorY, phase, amber);

            float menuAlpha = phase > 0.18f && phase < 0.86f ? 0.82f * (1.0f - std::fabs(phase - 0.52f) / 0.34f) : 0.0f;
            if (menuAlpha > 0.0f)
            {
                ID2D1SolidColorBrush* menuBrush = nullptr;
                ID2D1SolidColorBrush* strokeBrush = nullptr;
                target->CreateSolidColorBrush(D2D1::ColorF(0.15f, 0.15f, 0.15f, menuAlpha), &menuBrush);
                target->CreateSolidColorBrush(D2D1::ColorF(0.95f, 0.72f, 0.32f, 0.22f * menuAlpha), &strokeBrush);
                FloatRect menu = { cursorX + 20.0f, cursorY - 8.0f, 78.0f, 54.0f };
                target->FillRoundedRectangle(RoundedRect(menu, 6.0f), menuBrush);
                target->DrawRoundedRectangle(RoundedRect(menu, 6.0f), strokeBrush, 1.0f);
                target->DrawLine(D2D1::Point2F(menu.x + 12.0f, menu.y + 18.0f), D2D1::Point2F(menu.x + menu.w - 12.0f, menu.y + 18.0f), strokeBrush, 1.0f);
                target->DrawLine(D2D1::Point2F(menu.x + 12.0f, menu.y + 34.0f), D2D1::Point2F(menu.x + menu.w - 12.0f, menu.y + 34.0f), strokeBrush, 1.0f);
                SafeRelease(strokeBrush);
                SafeRelease(menuBrush);
            }
            return;
        }

        if (key == L"drag")
        {
            double loop = std::fmod(elapsed, 2.4);
            float phase = static_cast<float>(std::clamp((loop - 0.22) / 1.18, 0.0, 1.0));
            DrawDragCurve(target, cursorX - 82.0f, cursorY + 58.0f, cursorX, cursorY, phase);
            if (loop > 1.52 && loop < 2.05)
                DrawPulse(target, cursorX, cursorY, static_cast<float>((loop - 1.52) / 0.53), teal);
        }
    }

    void RenderFrame()
    {
        if (!g_hwnd || !g_hasFrame)
            return;

        FrameState frame = g_frame;
        SetWindowPos(g_hwnd, HWND_TOPMOST, frame.x, frame.y, frame.width, frame.height, SWP_NOACTIVATE | SWP_SHOWWINDOW);

        IWICBitmap* bitmap = nullptr;
        ID2D1RenderTarget* renderTarget = nullptr;
        if (FAILED(g_wicFactory->CreateBitmap(frame.width, frame.height, GUID_WICPixelFormat32bppPBGRA,
            WICBitmapCacheOnLoad, &bitmap)))
        {
            return;
        }

        D2D1_RENDER_TARGET_PROPERTIES properties = D2D1::RenderTargetProperties(
            D2D1_RENDER_TARGET_TYPE_DEFAULT,
            D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED),
            96.0f,
            96.0f);

        if (FAILED(g_d2dFactory->CreateWicBitmapRenderTarget(bitmap, properties, &renderTarget)))
        {
            SafeRelease(bitmap);
            return;
        }

        ID2D1SolidColorBrush* dim = nullptr;
        ID2D1SolidColorBrush* cardBrush = nullptr;
        ID2D1SolidColorBrush* cardStroke = nullptr;
        ID2D1SolidColorBrush* titleBrush = nullptr;
        ID2D1SolidColorBrush* mutedBrush = nullptr;
        ID2D1SolidColorBrush* spotlightStroke = nullptr;

        renderTarget->CreateSolidColorBrush(D2D1::ColorF(0, 0, 0, 0.38f), &dim);
        renderTarget->CreateSolidColorBrush(D2D1::ColorF(0.137f, 0.137f, 0.137f, 0.98f), &cardBrush);
        renderTarget->CreateSolidColorBrush(D2D1::ColorF(1.0f, 1.0f, 1.0f, 0.08f), &cardStroke);
        renderTarget->CreateSolidColorBrush(D2D1::ColorF(0.94f, 0.94f, 0.94f, 1.0f), &titleBrush);
        renderTarget->CreateSolidColorBrush(D2D1::ColorF(0.565f, 0.565f, 0.565f, 1.0f), &mutedBrush);
        renderTarget->CreateSolidColorBrush(D2D1::ColorF(0.211f, 0.749f, 0.694f, 0.72f), &spotlightStroke);

        FloatRect desiredSpotlight = ToFloatRect(ClampSpotlight(frame));
        FloatRect desiredCard = ToFloatRect(ComputeCardRect(frame, desiredSpotlight));
        float desiredCursorX = frame.targetResolved ? desiredSpotlight.x + desiredSpotlight.w * 0.5f : frame.width * 0.5f - 150.0f;
        float desiredCursorY = frame.targetResolved ? desiredSpotlight.y + desiredSpotlight.h * 0.5f : frame.height * 0.5f - 20.0f;
        RetargetAnimation(frame.Key(), desiredSpotlight, desiredCard, desiredCursorX, desiredCursorY);
        UpdateAnimation();

        FloatRect spotlight = g_animation.spotlightCurrent;
        FloatRect card = g_animation.cardCurrent;
        SetCardControls(ToRectI(card));

        renderTarget->BeginDraw();
        renderTarget->Clear(D2D1::ColorF(0, 0));

        bool unintrusive = IsUnintrusiveMode(frame);
        if (!unintrusive && frame.targetResolved)
        {
            renderTarget->FillRectangle(RectF(0, 0, static_cast<float>(frame.width), spotlight.y), dim);
            renderTarget->FillRectangle(RectF(0, spotlight.y + spotlight.h,
                static_cast<float>(frame.width), static_cast<float>(frame.height) - spotlight.y - spotlight.h), dim);
            renderTarget->FillRectangle(RectF(0, spotlight.y, spotlight.x, spotlight.h), dim);
            renderTarget->FillRectangle(RectF(spotlight.x + spotlight.w, spotlight.y,
                static_cast<float>(frame.width) - spotlight.x - spotlight.w, spotlight.h), dim);
            renderTarget->DrawRoundedRectangle(RoundedRect(spotlight, 8.0f), spotlightStroke, 1.4f);
        }
        else if (!unintrusive)
        {
            renderTarget->FillRectangle(RectF(0, 0, static_cast<float>(frame.width), static_cast<float>(frame.height)), dim);
        }

        float actionAnchorX = g_animation.cursorCurrentX;
        float actionAnchorY = g_animation.cursorCurrentY;
        float cursorX = actionAnchorX;
        float cursorY = actionAnchorY;
        ApplyActionCursorMotion(frame.mouseAction, SecondsSinceStepStart(), cursorX, cursorY);
        DrawMouseAction(renderTarget, frame.mouseAction, actionAnchorX, actionAnchorY);
        DrawCursor(renderTarget, cursorX, cursorY);

        FillRounded(renderTarget, cardBrush, g_cardRect, 8.0f);
        renderTarget->DrawRoundedRectangle(RoundedRect(g_cardRect, 8.0f), cardStroke, 1.0f);

        std::wstring heading = frame.stepTitle.empty() ? frame.tutorialTitle : frame.stepTitle;
        std::wstring body = frame.targetResolved ? frame.stepText : L"Target could not be resolved. " + frame.stepText;

        DrawTextBlock(renderTarget, g_titleFormat, titleBrush, heading,
            RectF(static_cast<float>(g_cardRect.x + 18), static_cast<float>(g_cardRect.y + 14),
                static_cast<float>(g_cardRect.w - 112), 30.0f));
        DrawTextBlock(renderTarget, g_smallFormat, mutedBrush, frame.counter,
            RectF(static_cast<float>(g_cardRect.x + g_cardRect.w - 78), static_cast<float>(g_cardRect.y + 18), 62.0f, 18.0f));
        DrawTextBlock(renderTarget, g_bodyFormat, titleBrush, body,
            RectF(static_cast<float>(g_cardRect.x + 18), static_cast<float>(g_cardRect.y + 52),
                static_cast<float>(g_cardRect.w - 36), 62.0f));
        DrawTextBlock(renderTarget, g_smallFormat, mutedBrush, frame.wait.empty() ? L"Use Next when ready" : frame.wait,
            RectF(static_cast<float>(g_cardRect.x + 18), static_cast<float>(g_cardRect.y + g_cardRect.h - 68),
                static_cast<float>(g_cardRect.w - 36), 20.0f));

        ID2D1SolidColorBrush* closeBrush = nullptr;
        renderTarget->CreateSolidColorBrush(D2D1::ColorF(0.565f, 0.565f, 0.565f, 1.0f), &closeBrush);
        renderTarget->DrawLine(D2D1::Point2F(static_cast<float>(g_closeButtonRect.x + 6), static_cast<float>(g_closeButtonRect.y + 6)),
            D2D1::Point2F(static_cast<float>(g_closeButtonRect.x + g_closeButtonRect.w - 6), static_cast<float>(g_closeButtonRect.y + g_closeButtonRect.h - 6)),
            closeBrush, 2.0f);
        renderTarget->DrawLine(D2D1::Point2F(static_cast<float>(g_closeButtonRect.x + g_closeButtonRect.w - 6), static_cast<float>(g_closeButtonRect.y + 6)),
            D2D1::Point2F(static_cast<float>(g_closeButtonRect.x + 6), static_cast<float>(g_closeButtonRect.y + g_closeButtonRect.h - 6)),
            closeBrush, 2.0f);
        SafeRelease(closeBrush);

        DrawButton(renderTarget, g_backButtonRect, L"Back", false, frame.canBack);
        DrawButton(renderTarget, g_nextButtonRect, frame.isLast ? L"Done" : L"Next", true, true);

        HRESULT drawResult = renderTarget->EndDraw();
        if (SUCCEEDED(drawResult))
            CopyWicBitmapToLayeredWindow(bitmap, frame.width, frame.height, frame.x, frame.y);

        SafeRelease(spotlightStroke);
        SafeRelease(mutedBrush);
        SafeRelease(titleBrush);
        SafeRelease(cardStroke);
        SafeRelease(cardBrush);
        SafeRelease(dim);
        SafeRelease(renderTarget);
        SafeRelease(bitmap);
    }

    void UpdateParentProcess(DWORD pid)
    {
        if (pid == 0)
            return;

        if (g_parentProcess)
            return;

        g_parentProcess = OpenProcess(SYNCHRONIZE, FALSE, pid);
    }

    void StdinThread()
    {
        std::string line;
        while (g_running && std::getline(std::cin, line))
        {
            if (line == "STOP")
            {
                PostMessage(g_hwnd, WM_CLOSE, 0, 0);
                return;
            }

            FrameState parsed;
            if (ParseFrame(line, parsed))
            {
                {
                    std::lock_guard<std::mutex> guard(g_frameMutex);
                    g_pendingFrame = parsed;
                }
                PostMessage(g_hwnd, kFrameMessage, 0, 0);
            }
        }

        PostMessage(g_hwnd, WM_CLOSE, 0, 0);
    }

    LRESULT CALLBACK WndProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
        case kFrameMessage:
        {
            {
                std::lock_guard<std::mutex> guard(g_frameMutex);
                g_frame = g_pendingFrame;
                g_hasFrame = true;
            }

            std::wstring key = g_frame.Key();
            if (key != g_lastFrameKey)
            {
                g_lastFrameKey = key;
                g_stepStartedAt = std::chrono::steady_clock::now();
            }

            UpdateParentProcess(g_frame.parentPid);
            RenderFrame();
            UpdateClickThrough();
            return 0;
        }
        case WM_NCHITTEST:
        {
            int x = GET_X_LPARAM(lParam) - g_frame.x;
            int y = GET_Y_LPARAM(lParam) - g_frame.y;
            return IsPopupHit(x, y) ? HTCLIENT : HTTRANSPARENT;
        }
        case WM_SETCURSOR:
        {
            POINT cursor{};
            GetCursorPos(&cursor);
            int x = cursor.x - g_frame.x;
            int y = cursor.y - g_frame.y;
            if (!IsPopupHit(x, y))
                return FALSE;
            return DefWindowProc(hwnd, message, wParam, lParam);
        }
        case WM_MOUSEMOVE:
        case WM_RBUTTONDOWN:
        case WM_RBUTTONUP:
        case WM_MBUTTONDOWN:
        case WM_MBUTTONUP:
        case WM_MOUSEWHEEL:
        case WM_MOUSEHWHEEL:
        {
            int x = GET_X_LPARAM(lParam);
            int y = GET_Y_LPARAM(lParam);
            if (!IsPopupHit(x, y))
                return 0;
            return DefWindowProc(hwnd, message, wParam, lParam);
        }
        case WM_LBUTTONUP:
        {
            int x = GET_X_LPARAM(lParam);
            int y = GET_Y_LPARAM(lParam);
            if (!IsPopupHit(x, y))
                return 0;

            if (g_closeButtonRect.Contains(x, y))
                std::cout << "CLOSE\n" << std::flush;
            else if (g_backButtonRect.Contains(x, y) && g_frame.canBack)
                std::cout << "BACK\n" << std::flush;
            else if (g_nextButtonRect.Contains(x, y))
                std::cout << "NEXT\n" << std::flush;
            return 0;
        }
        case WM_TIMER:
        {
            if (g_parentProcess && WaitForSingleObject(g_parentProcess, 0) == WAIT_OBJECT_0)
            {
                DestroyWindow(hwnd);
                return 0;
            }
            RenderFrame();
            UpdateClickThrough();
            return 0;
        }
        case WM_CLOSE:
            DestroyWindow(hwnd);
            return 0;
        case WM_DESTROY:
            g_running = false;
            if (g_parentProcess)
            {
                CloseHandle(g_parentProcess);
                g_parentProcess = nullptr;
            }
            PostQuitMessage(0);
            return 0;
        default:
            return DefWindowProc(hwnd, message, wParam, lParam);
        }
    }

    bool InitializeRendering()
    {
        if (FAILED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED)))
            return false;

        if (FAILED(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, &g_d2dFactory)))
            return false;

        if (FAILED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
            reinterpret_cast<IUnknown**>(&g_dwriteFactory))))
        {
            return false;
        }

        if (FAILED(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(&g_wicFactory))))
        {
            return false;
        }

        g_dwriteFactory->CreateTextFormat(L"Segoe UI", nullptr, DWRITE_FONT_WEIGHT_SEMI_BOLD,
            DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, 21.0f, L"", &g_titleFormat);
        g_dwriteFactory->CreateTextFormat(L"Segoe UI", nullptr, DWRITE_FONT_WEIGHT_NORMAL,
            DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, 16.0f, L"", &g_bodyFormat);
        g_dwriteFactory->CreateTextFormat(L"Segoe UI", nullptr, DWRITE_FONT_WEIGHT_NORMAL,
            DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, 13.0f, L"", &g_smallFormat);

        return g_titleFormat && g_bodyFormat && g_smallFormat;
    }

    void ShutdownRendering()
    {
        SafeRelease(g_smallFormat);
        SafeRelease(g_bodyFormat);
        SafeRelease(g_titleFormat);
        SafeRelease(g_cursorBitmapSource);
        SafeRelease(g_wicFactory);
        SafeRelease(g_dwriteFactory);
        SafeRelease(g_d2dFactory);
        CoUninitialize();
    }

}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int)
{
    if (!InitializeRendering())
        return 2;

    WNDCLASSEX wc{};
    wc.cbSize = sizeof(WNDCLASSEX);
    wc.lpfnWndProc = WndProc;
    wc.hInstance = instance;
    wc.lpszClassName = kWindowClass;
    RegisterClassEx(&wc);

    DWORD exStyle = WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
    g_hwnd = CreateWindowEx(exStyle, kWindowClass, L"YUCP Companion Tutorial",
        WS_POPUP, 0, 0, 1, 1, nullptr, nullptr, instance, nullptr);
    if (!g_hwnd)
    {
        ShutdownRendering();
        return 3;
    }

    SetTimer(g_hwnd, 1, 16, nullptr);
    std::thread inputThread(StdinThread);

    MSG message;
    while (GetMessage(&message, nullptr, 0, 0) > 0)
    {
        TranslateMessage(&message);
        DispatchMessage(&message);
    }

    g_running = false;
    if (inputThread.joinable())
        inputThread.detach();

    ShutdownRendering();
    return 0;
}
