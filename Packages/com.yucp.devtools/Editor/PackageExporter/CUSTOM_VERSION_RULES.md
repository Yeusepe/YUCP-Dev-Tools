# Custom Version Rules

The YUCP Package Exporter allows you to create custom version bumping rules through the UI. This gives you full control over how version numbers are incremented in your packages.

## Creating a Custom Rule

### Method 1: Through the Asset Menu

1. Right-click in the Project window
2. Select **Create → YUCP → Custom Version Rule**
3. Name your rule asset (e.g., `MyVersionRule`)
4. The asset will be created and selected

### Method 2: Through the Export Profile

1. Open your Export Profile
2. Enable **Auto-Increment Version**
3. Click the **Create** button next to "Custom Rule"
4. The rule will be automatically created and assigned to your profile

## Configuring a Custom Rule

Select your Custom Version Rule asset to see the inspector:

### Basic Settings

- **Rule Name**: Unique identifier (lowercase, no spaces). This is what you use in `@bump` directives.
- **Display Name**: Human-readable name shown in the UI
- **Description**: What this rule does

### Pattern Configuration

- **Regex Pattern**: The regular expression to match version strings
- **Rule Type**: Base behavior (Semver, DottedTail, WordNum, Build, CalVer, Number, Custom)
- **Supports Parts**: Whether the rule understands major/minor/patch
- **Preserve Padding**: Keep zero padding in numbers (e.g., `007` → `008`)

### Testing

- **Example Input**: Test version string
- **Example Output**: Expected result after bumping
- **Test Rule** button: Validates your configuration
- **Register Rule** button: Makes the rule available for use

## Built-in Rule Types

### 1. Semver (Semantic Versioning)
Standard `MAJOR.MINOR.PATCH` versioning.

```
Pattern: \b(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)\b
Example: 1.0.0 → 1.0.1 (patch)
         1.0.0 → 1.1.0 (minor)
         1.0.0 → 2.0.0 (major)
```

### 2. DottedTail
Increments the last component of a dotted version.

```
Pattern: \b(?<prefix>(?:\d+\.)*)(?<last>[A-Za-z]|\d+)\b
Example: 1.2.9 → 1.2.10
         1.0.a → 1.0.b
```

### 3. WordNum
Word followed by a number.

```
Pattern: \b(?<name>[A-Za-z]+)(?<num>\d+)\b
Example: VERSION1 → VERSION2
         BUILD042 → BUILD043
```

### 4. Build (4-part version)
Four-part version with build number.

```
Pattern: \b(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)\.(?<build>\d+)\b
Example: 1.0.0.0 → 1.0.0.1 (build)
         1.0.0.0 → 1.0.1.0 (patch)
```

### 5. CalVer (Calendar Versioning)
Date-based versioning.

```
Pattern: \b(?<year>\d{4})\.(?<month>\d{1,2})\.(?<day>\d{1,2})\b
Example: 2025.11.3 → 2025.11.4
```

### 6. Number
Simple number increment.

```
Pattern: \b(?<num>\d+)\b
Example: 42 → 43
         007 → 008 (with padding)
```

## Custom Rule Examples

### Example 1: Unity Version Format

```
Rule Name: unity_version
Pattern: \b(?<year>\d{4})\.(?<major>\d+)\.(?<minor>\d+)(?<letter>[a-z]\d+)?\b
Rule Type: Custom
Example Input: 2023.2.1f1
Example Output: 2023.2.2f1
```

### Example 2: Release Candidate

```
Rule Name: release_candidate
Pattern: \b(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)-rc(?<rc>\d+)\b
Rule Type: Semver
Example Input: 1.0.0-rc1
Example Output: 1.0.0-rc2
```

### Example 3: Prefixed Version

```
Rule Name: v_prefix
Pattern: \bv(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)\b
Rule Type: Semver
Example Input: v1.0.0
Example Output: v1.0.1
```

### Example 4: Hex Version

```
Rule Name: hex_version
Pattern: \b0x(?<hex>[0-9A-Fa-f]+)\b
Rule Type: Number
Example Input: 0x2A
Example Output: 0x2B
```

## Using Custom Rules

### In Export Profiles

1. Open your Export Profile
2. Enable **Auto-Increment Version**
3. Select your custom rule from the **Version Rule** dropdown, OR
4. Assign your custom rule asset to the **Custom Rule** field

The custom rule will be used when exporting the package.

### In @bump Directives

```csharp
public const string Version = "2023.2.1f1"; // @bump unity_version
public const string ReleaseCandidate = "1.0.0-rc1"; // @bump release_candidate
public const string Tagged = "v1.0.0"; // @bump v_prefix
```

## Regex Named Groups

The regex pattern should use named groups to capture version components:

### Common Group Names

- `major`, `minor`, `patch`: Semantic version parts
- `build`: Build number (4th part)
- `name`: Word part in WordNum rules
- `num`: Number part
- `prefix`: Prefix before the last component
- `last`: Last component in dotted versions
- `year`, `month`, `day`: Calendar version parts
- `rc`: Release candidate number
- `pre`: Pre-release identifier

### Regex Tips

1. **Word Boundaries**: Use `\b` to match whole words
2. **Digits**: `\d+` matches one or more digits
3. **Optional Groups**: `(?:...)?` makes a group optional
4. **Named Capture**: `(?<name>...)` creates a named group
5. **Escape Dots**: Use `\.` to match literal periods

## Advanced: Custom Rule Type

For complex versioning logic that can't be expressed with built-in types, use `Custom` rule type:

1. Create your CustomVersionRule asset
2. Set Rule Type to **Custom**
3. Create a C# class that extends CustomVersionRule:

```csharp
using YUCP.DevTools.Editor.PackageExporter;

public class MyAdvancedVersionRule : CustomVersionRule
{
    // Override CreateCustomBumpFunc() to implement custom logic
    protected override Func<Match, VersionBumpOptions, string> CreateCustomBumpFunc()
    {
        return (match, opt) =>
        {
            // Your custom version bumping logic here
            // Parse the match, increment as needed, return new version
            return newVersion;
        };
    }
}
```

## Testing Rules

### In the Inspector

1. Select your Custom Version Rule asset
2. Set **Example Input** to a test version
3. Set **Example Output** to the expected result
4. Click **Test Rule**
5. Check the result matches your expectation

### In the Package Exporter

1. Assign the rule to a profile
2. Use the manual bump buttons (Patch/Minor/Major) in the Version field
3. Check the version increments correctly

## Best Practices

1. **Unique Names**: Use descriptive, unique rule names
2. **Test Thoroughly**: Always test with multiple examples
3. **Document Patterns**: Add good descriptions
4. **Version Control**: Check custom rules into version control
5. **Share Rules**: Custom rules can be shared across projects
6. **Keep It Simple**: Use built-in types when possible
7. **Validate Regex**: Test regex patterns at regex101.com

## Troubleshooting

### Rule Not Found

- Click **Register Rule** in the inspector
- Check the rule name has no spaces or special characters
- Restart Unity if the rule still doesn't appear

### Wrong Output

- Verify your regex pattern matches the input
- Check named groups are spelled correctly
- Use regex101.com to debug your pattern
- Ensure Rule Type matches your pattern structure

### Test Failed

- Compare actual vs expected output
- Check if preserve padding is configured correctly
- Verify the rule type supports your use case

## Example Workflow

1. **Identify Your Format**: Determine your version string format
2. **Choose Rule Type**: Pick the closest built-in type
3. **Write Pattern**: Create regex with named groups
4. **Create Asset**: Use Assets → Create → YUCP → Custom Version Rule
5. **Configure**: Set all fields, especially pattern and examples
6. **Test**: Click Test Rule to validate
7. **Assign**: Add to your Export Profile
8. **Use**: Add `@bump` directives to your files
9. **Export**: Version bumps happen automatically!

## Built-in Rules Reference

You can always fall back to these built-in rules:

| Rule Name | Use Case | Example |
|-----------|----------|---------|
| `semver` | Standard versioning | 1.0.0 → 1.0.1 |
| `dotted_tail` | Increment last part | 1.2.9 → 1.2.10 |
| `wordnum` | Word + number | BUILD001 → BUILD002 |
| `build` | 4-part version | 1.0.0.0 → 1.0.0.1 |
| `calver` | Calendar dates | 2025.11.3 → 2025.11.4 |
| `number` | Simple numbers | 42 → 43 |

These are always available and don't require custom rules.









