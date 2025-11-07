# Custom Version Rule Examples

This folder contains example custom version rules you can use or modify for your projects.

## Available Examples

### 1. Unity Version Format (`UnityVersionRule.asset`)
**Pattern:** `2023.2.1f1` → `2023.2.2f1`

Use this for Unity-style version numbers with year, major, minor, and letter suffix.

```csharp
// Example usage in code:
public const string UnityVersion = "2023.2.1f1"; // @bump unity_version
```

### 2. Release Candidate (`ReleaseCandidateRule.asset`)
**Pattern:** `1.0.0-rc1` → `1.0.0-rc2`

Perfect for tracking release candidate versions during your testing phase.

```csharp
// Example usage:
public const string Version = "1.0.0-rc1"; // @bump release_candidate
```

### 3. Prefixed Version (`PrefixedVersionRule.asset`)
**Pattern:** `v1.0.0` → `v1.0.1`

For versions with a 'v' prefix, common in Git tags and releases.

```csharp
// Example usage:
public const string Version = "v1.0.0"; // @bump v_prefix
```

### 4. Date-Based Version (`DateBasedVersionRule.asset`)
**Pattern:** `2024.11.04` → `2024.11.05`

Calendar versioning that increments by day. Great for daily builds.

```csharp
// Example usage:
public const string BuildDate = "2024.11.04"; // @bump date_version
```

### 5. Build Number (`BuildNumberRule.asset`)
**Pattern:** `BUILD0042` → `BUILD0043`

Increments build numbers with zero padding preserved.

```csharp
// Example usage:
public const string Build = "BUILD0042"; // @bump build_number
```

### 6. Alpha Version (`AlphaVersionRule.asset`)
**Pattern:** `1.0.0-alpha.1` → `1.0.0-alpha.2`

For alpha releases with dot-separated alpha numbers.

```csharp
// Example usage:
public const string Version = "1.0.0-alpha.1"; // @bump alpha_version
```

## How to Use These Examples

### Method 1: Direct Assignment
1. Open your Export Profile in the Package Exporter
2. Enable **Auto-Increment Version**
3. Assign one of these example rules to **Custom Rule (Optional)**
4. Done! It will use that format when bumping versions

### Method 2: Create Your Own
1. Right-click one of these examples
2. Select **Duplicate**
3. Rename and modify it for your needs
4. Assign it to your profile

### Method 3: From Scratch
1. In Package Exporter, click **New** next to Custom Rule
2. Look at these examples for reference
3. Configure your own pattern and behavior

## Creating Your Own Rules

Each rule has these key fields:

- **Rule Name**: Used in `@bump` directives (lowercase, no spaces)
- **Display Name**: Human-readable name
- **Regex Pattern**: Pattern to match your version string
- **Rule Type**: Base behavior (Semver, WordNum, Number, etc.)
- **Example Input/Output**: For testing

### Tips:

1. **Start Simple**: Begin with a similar example and modify it
2. **Test First**: Always test your rule before using it
3. **Use Named Groups**: Your regex should have named groups like `(?<major>\d+)`
4. **Common Groups**: `major`, `minor`, `patch`, `num`, `name`, `year`, `month`, `day`

## Rule Types Explained

- **Semver (0)**: Standard semantic versioning with major.minor.patch
- **DottedTail (1)**: Increments the last number in a dotted sequence
- **WordNum (2)**: Word followed by a number (like VERSION1)
- **Build (3)**: Four-part versions (major.minor.patch.build)
- **CalVer (4)**: Calendar-based versioning
- **Number (5)**: Simple number increment
- **Custom (6)**: Advanced - requires code implementation

## Testing Your Rules

In the Package Exporter window:
1. Select your profile
2. Assign your custom rule
3. The inline editor appears below
4. Enter test input/output
5. Click **Test Rule**
6. Green = pass, Red = fail

## Common Patterns

### Hex Numbers
```
Pattern: \b0x(?<hex>[0-9A-Fa-f]+)\b
Example: 0x2A → 0x2B
```

### Dot-separated Letters
```
Pattern: \b(?<major>\d+)\.(?<minor>[a-z])\b
Example: 1.a → 1.b
```

### Year.Week Format
```
Pattern: \b(?<year>\d{4})\.(?<week>\d{1,2})\b
Example: 2024.42 → 2024.43
```

## Need Help?

- Check the regex pattern matches your version string
- Use [regex101.com](https://regex101.com) to test patterns
- Look at the console for detailed error messages
- Compare your rule to the working examples

## Sharing Rules

These rule assets are just files - you can:
- Share them with your team
- Check them into version control
- Use them across multiple projects
- Distribute them in packages

## Advanced: Custom Code

For complex versioning logic, set Rule Type to **Custom** and create a subclass:

```csharp
public class MyAdvancedRule : CustomVersionRule
{
    protected override Func<Match, VersionBumpOptions, string> CreateCustomBumpFunc()
    {
        return (match, opt) =>
        {
            // Your custom logic here
            return newVersion;
        };
    }
}
```

Assign your subclass asset to use the custom implementation.




