# Configuration

## Precedence

For each setting, the first source that sets it wins:

1. Command-line options
2. Environment variables
3. The configuration file (`--config <file>`, or `ribatiq.json` in the current directory)
4. Defaults

Two rules are deliberate exceptions:

- **Exclusions add up**: `bin/`, `obj/` and `.git/` are always excluded, plus the exclusions of every source.
- **`--no-ai` always wins**: it cannot be combined with `--provider` or `--model` on the command line,
  and it disables AI even if the environment or the file sets a provider.
- **Rule lists are applied in order**: the file's `enableRules` and `disableRules` first, then
  `--enable-rule` and `--disable-rule`, so the command line can turn a rule back on or off.

## Settings

| Setting | Command line | Environment | `ribatiq.json` | Default |
|---------|--------------|-------------|----------------|---------|
| AI provider | `--provider` | `RIBATIQ_PROVIDER` | `ai.provider` | none (AI off) |
| Model | `--model` | `RIBATIQ_MODEL` | `ai.model` | provider default |
| AI on/off | `--no-ai` | | `ai.enabled` | on when a provider is set |
| Failure threshold | `--fail-on` | `RIBATIQ_FAIL_ON` | `analysis.failOn` | `none` |
| Included files | `--include` | | `analysis.include` | all `*.cs` files |
| Excluded files | `--exclude` | | `analysis.exclude` | `**/bin/**`, `**/obj/**`, `**/.git/**` |
| Review profile | `--profile` | | `review.profile` | `default` |
| Rules to enable | `--enable-rule` | | `review.enableRules` | none |
| Rules to disable | `--disable-rule` | | `review.disableRules` | none |
| Minimum reported severity | `--min-severity` | | `review.minimumSeverity` | `info` |
| Project instructions | `--instructions` | | `review.instructions` (relative to the file) | `review-rules.md` at the root |
| NuGet vulnerability audit | `--audit-dependencies` (scan only) | | `review.auditDependencies` | off |
| Report format | `--format` | | | `console` |
| Report file | `--output` | | | standard output |

API keys are read only from `OPENAI_API_KEY`, `ANTHROPIC_API_KEY` and `GEMINI_API_KEY`.

## `ribatiq.json`

```json
{
  // Comments and trailing commas are allowed.
  "version": 1,
  "analysis": {
    "include": ["src/**/*.cs"],
    "exclude": ["**/Migrations/**", "**/*.g.cs"],
    "failOn": "high"
  },
  "ai": {
    "enabled": true,
    "provider": "openai",
    "model": "gpt-6.2-astra"
  },
  "review": {
    "profile": "security",
    "enableRules": ["CLEANCODE.NAMING"],
    "disableRules": ["TESTING.MISSING_TESTS"],
    "minimumSeverity": "low",
    "instructions": "docs/review-rules.md",
    "auditDependencies": true
  }
}
```

- `version` is required and must be `1`.
- Unknown settings are rejected (this also prevents storing API keys in the file).
- Errors name the file, the line and the setting, and exit with code 2.

Rules, profiles and `review-rules.md` are described in [Review rules](review-rules.md).

A commented example is available in [`samples/ribatiq.json`](../samples/ribatiq.json).

## Globs

Patterns match repository-relative paths with `/` separators, case-insensitively.

| Pattern | Matches |
|---------|---------|
| `*` | Any characters within one path segment. |
| `?` | One character within a segment. |
| `**` | Any number of segments, including none: `**/bin/**` matches `bin/a.cs` and `src/App/bin/a.cs`. |
| no `/` | Any depth: `*.g.cs` is treated as `**/*.g.cs`. |

## Limits

| Limit | Value |
|-------|-------|
| Source files per scan | 5,000 (first files in path order) |
| File size | 1 MB (larger files are skipped with a warning) |
| Changed files per review | 500 |
| AI context per request | 100,000 characters (40,000 per file) |
| AI context lines around a change | 20 |
| AI findings per response | 50 |
