# Changelog

All notable changes to RibatIQ are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [0.1.0] - Unreleased

First MVP release.

### Added

- `ribat scan <path>`: static analysis of a `.sln`, `.slnx`, `.csproj` or directory, with include
  and exclude globs.
- `ribat review --base <rev> [--head <rev>]`: review of the changes since the merge base, reading
  files at the head revision and reporting only findings on changed lines.
- Roslyn-based analysis without building: C# syntax errors and rules `RIQ1001` (empty catch),
  `RIQ1002` (async void), `RIQ1003` (`throw ex;`) and `RIQ1004` (possible hard-coded secret).
- Optional AI analysis with OpenAI (`gpt-6.2-astra` by default) or Anthropic (`claude-opus-5-5` by
  default) through their official SDKs, with bounded context, prompt-injection safeguards and strict
  validation of model output.
- Console and JSON reports (`schemaVersion` 2: catalogue and tool rule IDs, evidence, check outcomes), `--output` to a file.
- `--fail-on` threshold applied after the report is written; documented exit codes 0-3.
- Configuration through command-line options, `RIBATIQ_*` environment variables and `ribatiq.json`.
- Stable, versioned finding fingerprints.
- GitHub Actions workflow (Ubuntu and Windows) and Dependabot.

### Known limitations

- C# only; no semantic diagnostics that need project references.
- No suppression or baseline mechanism yet.
- No pull request comment publishing yet.
