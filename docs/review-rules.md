# Review rules

Every finding RibatIQ reports belongs to a rule from the catalogue in
[`rules/catalog.json`](../rules/catalog.json), or is explicitly marked as unclassified. Rules decide what is
checked, by which detector, and how severe a finding is by default.

## Detectors

| Detector | What it is | When it runs |
|----------|------------|--------------|
| `static-analysis` | Roslyn syntax errors and the RibatIQ analyzers (RIQ1001-RIQ1004), in-process, without a build. | Always, unless every static rule is disabled. |
| `dependency-audit` | `dotnet list package --vulnerable --include-transitive --no-restore`. | `scan --audit-dependencies` only. Projects must already be restored: RibatIQ never restores. |
| `ai-review` | The configured LLM provider, prompted with the enabled rules. | When a provider is configured and at least one AI rule is enabled. |

When the static analysis and the AI review report the same rule on overlapping lines of a file, the
findings are merged and the deterministic one is kept.

## Catalogue

| Rule | Category | Default severity | Detector | Enabled by default | Title |
|------|----------|------------------|----------|--------------------|-------|
| `CSHARP.SYNTAX.ERROR` | correctness | high | static-analysis | yes | C# syntax error |
| `DOTNET.EXCEPTIONS.EMPTY_CATCH` | correctness | medium | static-analysis | yes | Empty catch block |
| `DOTNET.EXCEPTIONS.RETHROW_LOSES_STACK` | correctness | low | static-analysis | yes | Rethrow loses the stack trace |
| `DOTNET.ASYNC.ASYNC_VOID` | bestPractices | medium | static-analysis | yes | Async void method |
| `SECURITY.EXPOSED_SECRET` | security | high | static-analysis | yes | Possible hard-coded secret |
| `DEPENDENCY.NUGET.VULNERABLE` | dependencies | high | dependency-audit | yes | Vulnerable NuGet package |
| `CORRECTNESS.NULL_REFERENCE` | correctness | high | ai-review | yes | Possible null reference |
| `CORRECTNESS.LOGIC_ERROR` | correctness | high | ai-review | yes | Logic error |
| `CORRECTNESS.BOUNDARY_CONDITION` | correctness | medium | ai-review | yes | Boundary or unexpected value |
| `CORRECTNESS.EXCEPTION_HANDLING` | correctness | medium | ai-review | yes | Incorrect exception handling |
| `CORRECTNESS.CONCURRENCY` | correctness | high | ai-review | yes | Concurrency issue |
| `DOTNET.DISPOSAL.RESOURCE_LEAK` | bestPractices | medium | ai-review | yes | Resource leak or incorrect disposal |
| `DOTNET.ASYNC.UNAWAITED` | bestPractices | medium | ai-review | yes | Unawaited task |
| `DOTNET.ASYNC.BLOCKING_WAIT` | bestPractices | medium | ai-review | yes | Blocking on asynchronous code |
| `DOTNET.ASYNC.CANCELLATION` | bestPractices | low | ai-review | yes | Cancellation token not propagated |
| `PERFORMANCE.NPLUSONE` | performance | medium | ai-review | yes | N+1 queries or repeated external calls |
| `PERFORMANCE.UNBOUNDED_QUERY` | performance | medium | ai-review | yes | Unbounded data retrieval |
| `PERFORMANCE.LINQ_INEFFICIENT` | performance | low | ai-review | yes | Inefficient LINQ or repeated enumeration |
| `PERFORMANCE.STRING_CONCAT_IN_LOOP` | performance | low | ai-review | yes | String concatenation in a loop |
| `PERFORMANCE.ALLOCATIONS` | performance | low | ai-review | yes | Unnecessary allocations or expensive operations |
| `SECURITY.SQL_INJECTION` | security | high | ai-review | yes | SQL injection |
| `SECURITY.INJECTION` | security | high | ai-review | yes | Command or other injection |
| `SECURITY.PATH_TRAVERSAL` | security | high | ai-review | yes | Path traversal |
| `SECURITY.UNSAFE_INPUT` | security | medium | ai-review | yes | Unsafe handling of external input |
| `SECURITY.ACCESS_CONTROL` | security | high | ai-review | yes | Authorization or access-control flaw |
| `SECURITY.INSECURE_CRYPTO` | security | medium | ai-review | yes | Insecure cryptography |
| `SECURITY.INSECURE_DESERIALIZATION` | security | high | ai-review | yes | Insecure deserialization |
| `CLEANCODE.COMPLEXITY` | cleanCode | low | ai-review | yes | Unnecessary complexity |
| `CLEANCODE.DUPLICATION` | cleanCode | low | ai-review | yes | Duplicated logic |
| `CLEANCODE.DEAD_CODE` | cleanCode | info | ai-review | yes | Dead code or unused variables |
| `CLEANCODE.NAMING` | cleanCode | info | ai-review | no | Misleading naming |
| `ARCHITECTURE.LAYER_VIOLATION` | architecture | medium | ai-review | yes | Incorrect dependency direction |
| `ARCHITECTURE.RESPONSIBILITIES` | architecture | low | ai-review | yes | Too many responsibilities |
| `ARCHITECTURE.COUPLING` | architecture | low | ai-review | no | Excessive coupling |
| `ARCHITECTURE.UNNECESSARY_ABSTRACTION` | architecture | info | ai-review | no | Unnecessary abstraction |
| `TESTING.MISSING_TESTS` | testing | low | ai-review | yes | Changed behavior without tests |
| `TESTING.FRAGILE_TEST` | testing | low | ai-review | yes | Fragile or nondeterministic test |
| `PRODUCTION.MISSING_TIMEOUT` | productionReadiness | medium | ai-review | yes | Missing timeout or resilience |
| `PRODUCTION.ERROR_HANDLING_LOGGING` | productionReadiness | low | ai-review | yes | Inadequate logging or error handling |
| `PRODUCTION.CONFIGURATION` | productionReadiness | medium | ai-review | yes | Unsafe environment-specific configuration |
| `PRODUCTION.HEALTH_CHECKS` | productionReadiness | info | ai-review | no | Missing health or readiness checks |

AI findings are capped at `high`: only deterministic detectors report `critical` findings. The AI may
also report a finding that matches no rule; it is reported as unclassified, without a rule ID.

## Profiles

| Profile | Rules |
|---------|-------|
| `default` | Every rule enabled by default. |
| `security` | Enabled rules in the `security` and `dependencies` categories. |
| `performance` | Enabled rules in the `performance` category. |

A profile also filters findings: with `security`, an AI finding in another category is not reported,
unless you enabled a rule of that category explicitly.

## Choosing rules

```bash
ribat scan . --profile security
ribat review --base origin/main --provider openai --enable-rule CLEANCODE.NAMING --disable-rule TESTING.MISSING_TESTS
ribat scan . --min-severity medium
```

The same settings exist in `ribatiq.json`, in the `review` section (see [Configuration](configuration.md)).
Rule lists are applied from the configuration file to the command line, so `--enable-rule` can turn back
on a rule the file disables. Rule IDs are case-insensitive; an unknown rule, or a rule both enabled and
disabled in the same place, is a configuration error (exit code 2).

## Project instructions: `review-rules.md`

A `review-rules.md` file at the root of the scanned directory, or of the repository at the `--head`
revision for `review`, describes project conventions for the AI review:

```markdown
# Review rules
- Domain types are records; services are sealed.
- Controllers must not reference Entity Framework directly.
- Every public API change needs a test in tests/Api.
```

Use `--instructions <file>` (or `review.instructions` in `ribatiq.json`) to read another file.

The file is **untrusted data**, like the code under review:

- It is sent to the model as context, between markers, never as instructions to RibatIQ.
- It cannot enable or disable rules, change severities, or skip a check. Only the command line and
  `ribatiq.json` can.
- It is limited to 64 KB on disk and 16,000 characters sent to the model (the rest is cut, with a warning).
  Empty, non-UTF-8, oversized or unreadable files are ignored with a warning; the review still runs.
- It is only used by the AI review. Without a provider, the report says it was found but not used.
- Its text never appears in reports.

## Checks and incomplete analysis

Each run records one entry per detector in the report, with its status:

| Status | Meaning |
|--------|---------|
| `completed` | The detector ran for the listed rules. No finding means none was detected, not that there is none. |
| `skipped` | The detector did not run, with the reason (not requested, no provider, rules disabled, projects not restored...). |
| `failed` | The detector was started but failed (for example, `dotnet` was not found). The run still completes. |

The console report lists the checks and ends with an *Incomplete analysis* line when one was skipped or
failed. The JSON report has a `checks` array and a top-level `complete` flag. Exit codes are not changed
by skipped or failed checks: a CI job that requires a check should test `complete` or the check's status.
