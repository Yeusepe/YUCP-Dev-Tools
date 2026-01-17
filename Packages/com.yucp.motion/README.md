# YUCP Motion - UI Toolkit Core Infrastructure

Allocation-free UI Toolkit infrastructure for Editor inspector UI, EditorWindow, and runtime UI. Core is Unity-free, a single global tick drives all controllers, Unity version quirks are isolated in one compat layer, and gestures use correct pointer capture + coordinate conversion.

## Features

- **Unity-free core**: Core types and systems have no Unity dependencies
- **Single global tick**: One tick source updates all controllers, never schedule per element
- **Allocation-free**: Zero allocations per frame in steady state
- **Works everywhere**: Inspector, EditorWindow, and Runtime UIDocument contexts
- **Version compatibility**: Unity version differences isolated in UiToolkitCompat
- **Gesture support**: Drag/Pan gestures with correct pointer math and capture

## Quick Start

```csharp
using YUCP.Motion;
using YUCP.Motion.Core;

// Initialize (usually done automatically on first Attach)
Motion.Initialize();

// Attach to any VisualElement
var handle = Motion.Attach(myVisualElement);

// Animate
handle.Animate(new MotionTargets
{
    HasX = true,
    X = 100,
    HasY = true,
    Y = 50,
    HasOpacity = true,
    Opacity = 0.8f
}, new Transition(0.5f, EasingType.EaseOut));

// Clean up when done
handle.Dispose();
```

## Package Structure

- **Runtime/Core**: Unity-free core types and systems
- **Runtime/Unity**: Unity adapter layer and UI Toolkit integration
- **Editor**: Editor-specific tick driver
- **Samples~**: Demo samples for Inspector, EditorWindow, and Runtime contexts

## Architecture

The system is designed with a clear separation:

1. **Core Layer** (Unity-free): ElementId, TransformState, ColorRGBA, MotionController, TickSystem
2. **Unity Adapter Layer**: MotionViewAdapter, StyleApplier, UiToolkitCompat
3. **Public API**: Motion.Attach(), MotionHandle
4. **Optional Sugar**: MotionElement for UXML support

## License

Part of YUCP Club packages.
