# Changelog

All notable changes to RibatIQ are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-11-22

First MVP release.

### Added

- `ribat scan <path>`: static analysis of a `.sln`, `.slnx`, `.csproj` or directory, with include
  and exclude globs.
- `ribat review --base <rev> [--head <rev>]`: review of the changes since the merge base, reading
  files at the head revision and reporting only findings on changed lines.
- Roslyn-based analysis without building: C# syntax errors and rules `RIQ1001` (empty catch),
  `RIQ1002` (async void), `RIQ1003` (`throw ex;`) and `RIQ1004` (possible hard-coded secret).
- Optional AI analysis with OpenAI (`gpt-6.2-astra` by default), Anthropic (`claude-opus-5-5` by
  default) or Gemini (`gemini-4-hydrogen` by default, through its OpenAI-compatible endpoint, with a
  fallback to plain JSON mode when strict schemas are rejected), with bounded context,
  prompt-injection safeguards and strict validation of model output.
- Console and JSON reports (`schemaVersion` 2: catalogue and tool rule IDs, evidence, check outcomes), `--output` to a file.
- `--fail-on` threshold applied after the report is written; documented exit codes 0-3.
- Configuration through command-line options, `RIBATIQ_*` environment variables and `ribatiq.json`.
- Stable, versioned finding fingerprints.
- GitHub Actions workflow (Ubuntu and Windows) and Dependabot.
- Review rules: a catalogue of 41 rules in nine categories (`rules/catalog.json`), each with an ID,
  a default severity, a detector (static analysis, dependency audit or AI review) and a default state.
- Review profiles `default`, `security` and `performance`; `--profile`, `--enable-rule`,
  `--disable-rule` and `--min-severity`, also available in the `review` section of `ribatiq.json`.
- Project instructions in `review-rules.md` (or `--instructions`), sent to the AI review as untrusted,
  size-limited context; they never change the review policy.
- The AI review is prompted with the enabled rules and must classify each finding with one of them;
  findings for other rules are rejected.
- Opt-in NuGet vulnerability audit (`scan --audit-dependencies`) through
  `dotnet list package --vulnerable`, without ever restoring.
- Static and AI findings for the same rule on overlapping lines are merged.
- Reports list every check as completed, skipped or failed, with the reason; the JSON report has
  `complete`, `policy` and `instructions` fields.
- Bounded Git and `dotnet` output; oversized changed files are skipped before being read.

### Fixed

- Roslyn findings are reported in a stable order.

### Known limitations

- C# only; no semantic diagnostics that need project references.
- No suppression or baseline mechanism yet: test files with fake keys are reported by
  `SECURITY.EXPOSED_SECRET`.
- No pull request comment publishing yet, and no SARIF output.
- Skipped or failed checks do not change the exit code; CI jobs that require every check must test
  the JSON report's `complete` field.
- Most catalogue rules are detected only by the AI review, so they are only checked when a provider is
  configured. Their results are heuristic.
- The dependency audit covers NuGet packages of restored projects only, and evaluates the
  repository's MSBuild files.
