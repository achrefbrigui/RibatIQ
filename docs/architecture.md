# Architecture

RibatIQ is a modular monolith: one command-line application split into four projects with a
strict dependency direction. Vendor SDKs, Git and the CLI framework never reach the core.

```text
RibatIQ.Cli  ──►  RibatIQ.Infrastructure  ──►  RibatIQ.Application  ──►  RibatIQ.Domain
(System.CommandLine)   (Roslyn, Git, OpenAI,        (use cases, ports)        (findings, results)
                        Anthropic, JSON)
```

`tests/RibatIQ.IntegrationTests/ArchitectureTests.cs` fails the build if Domain or Application
start referencing the CLI framework, provider SDKs or Infrastructure.

## Projects

| Project | Responsibility | Key types |
|---------|----------------|-----------|
| Domain | Validated findings, rules, policy and results; no dependencies. | `ReviewFinding`, `FindingFingerprint`, `RepositoryPath`, `ReviewRuleId`, `ReviewPolicy`, `ProjectInstructions`, `CheckResult`, `AnalysisResult` |
| Application | Workflows and the ports they need. | `ScanService`, `ReviewService`, `RuleCatalog`, `ReviewPolicyResolver`, `ProjectInstructionsLoader`, `FindingDeduplicator`, `AiAnalyzer`, `AiFindingParser`, `UnifiedDiffParser`, `SettingsResolver`, `ISourceDiscovery`, `IStaticAnalyzer`, `IDependencyAuditor`, `IInstructionsReader`, `IGitClient`, `ILlmProvider`, `IReportWriter` |
| Infrastructure | Adapters for the outside world. | `FileSystemSourceDiscovery`, `RoslynStaticAnalyzer`, `DotnetListPackageAuditor`, `FileSystemInstructionsReader`, `GitCli`, `OpenAiProvider`, `AnthropicProvider`, `JsonReportWriter` |
| Cli | Parsing, configuration sources, composition, console output, exit codes. | `RibatCli`, `ScanCommand`, `ReviewCommand`, `AnalysisOptions`, `ConfigFileLoader`, `ReportPublisher` |

## Pipeline

Both commands follow the same steps; only the source of documents differs.

1. **Input validation**: target path or Git revisions (`ScanRequest`, `ReviewRequest`), settings
   from command line, environment and `ribatiq.json` (`SettingsResolver`).
2. **Policy and provider**: the review policy is resolved from the rule catalogue, the profile and
   the enable/disable lists (`ReviewPolicyResolver`); when AI is enabled the provider is created.
   Unknown rules, unknown profiles and missing API keys fail here, before any work.
3. **Discovery**: `scan` walks the solution, project or directory; `review` runs
   `git diff base...head` and reads changed files at the head revision, after checking their size.
4. **Project instructions**: `review-rules.md` (or `--instructions`) is loaded as bounded, untrusted
   text (`ProjectInstructionsLoader`). It is attached to the policy but never changes it.
5. **Deterministic analysis**: Roslyn parses the files in memory and runs the RibatIQ analyzers.
   Findings are classified from the catalogue (rule ID, title, severity, category).
6. **AI analysis** (optional): bounded context, a prompt listing the enabled AI rules, the project
   instructions between markers, and a provider call.
7. **Dependency audit** (`scan --audit-dependencies`): `dotnet list package --vulnerable --no-restore`
   through `IDependencyAuditor`.
8. **Validation**: model output is parsed and every finding is checked against what was sent and
   against the enabled rules (`AiFindingParser`); review findings must touch changed lines
   (`DiffLocationValidator`).
9. **Normalization**: findings are de-duplicated (`FindingDeduplicator`: same fingerprint, or same rule
   on overlapping lines of a file; static analysis wins over AI), then filtered by the policy
   (rule enabled, category, minimum severity).
10. **Checks**: each detector records a `CheckResult` (completed, skipped or failed, with the reason),
    so a report never presents a check that did not run as clean.
11. **Reporting**: console or JSON, to standard output or a file.
12. **Exit policy**: the `--fail-on` threshold is applied after the report is written.

## Rules and policy

The catalogue (`rules/catalog.json`, embedded in Application) describes the supported rules and the
profiles; `RuleCatalog` validates it strictly at load time. Domain types own the invariants:
`ReviewRuleId` validates identifiers and `ReviewPolicy` decides what is enabled and reported. Each
detector declares the rules it covers (`IStaticAnalyzer.SupportedRules`,
`IDependencyAuditor.SupportedRules`), and the AI review receives only the enabled AI rules.

## Findings and fingerprints

A `ReviewFinding` is valid by construction: required text, bounded lengths, repository-relative
paths (no absolute paths, no `..`), consistent line ranges, confidence in [0, 1]. Its `Id` is a
versioned SHA-256 fingerprint of the file path, the rule (or normalized title) and the start line,
so the same issue keeps the same ID across runs and providers. This is the basis for a future
baseline feature.

## Error model

| Exception | Meaning | Exit code |
|-----------|---------|-----------|
| `ConfigurationException` | Invalid settings or missing API key. | 2 |
| `InvalidInputException` | Unusable target, solution file or Git revision. | 2 |
| `OperationFailedException` | Git, provider or I/O failure; malformed AI output. | 3 |
| `LlmProviderException` | Provider-level failure, wrapped into `OperationFailedException`. | 3 |

## Adding an LLM provider

1. Implement `ILlmProvider` in `RibatIQ.Infrastructure/Llm`, keeping SDK types internal and mapping
   failures to `LlmErrorKind` (see `ProviderErrors`).
2. Read the API key from one environment variable and throw `ConfigurationException` when it is missing.
3. Register the provider ID in `LlmProviders` and in `LlmProviderRegistry`.
4. Add a subclass of `LlmProviderContractTests` with the provider's wire formats.
