# AiSample fixture

Code with **intentional defects that only the AI review can find**. Nothing here triggers a static
analysis rule (a test checks it), so every finding on this fixture comes from the model. The code has
no hint comments on purpose: the expected answers are only in this file. It is analyzed as data and
never built.

Use it to check a provider by hand. Detection by a model is not deterministic, so this list is a
target, not an automated assertion:

```powershell
ribat scan tests/Fixtures/AiSample --provider anthropic
```

## Expected findings

| Location | Rule | Defect |
|----------|------|--------|
| `Data/UserRepository.cs:12` | `SECURITY.SQL_INJECTION` | `name` is concatenated into the SQL text instead of being a parameter. |
| `Data/UserRepository.cs:20` | `CORRECTNESS.NULL_REFERENCE` | `FirstOrDefault` can return `null`, then `user.Email` is dereferenced. |
| `Data/UserRepository.cs:25` | `SECURITY.INSECURE_CRYPTO` | Passwords hashed with unsalted MD5 instead of a password hashing function. |
| `Data/UserRepository.cs:30` | `PERFORMANCE.UNBOUNDED_QUERY` | `ToList()` loads the whole `Users` table before filtering in memory. |
| `Services/OrderService.cs:12` | `PERFORMANCE.NPLUSONE` | One `OrderLines` query per order inside the loop. |
| `Services/OrderService.cs:23` | `CORRECTNESS.BOUNDARY_CONDITION` | `i <= count` reads one element too many and ignores `lines.Count`. |
| `Services/OrderService.cs:33` | `CORRECTNESS.CONCURRENCY` | `s_processed++` on a static field from `Parallel.ForEach` is a data race. |
| `Services/ReportService.cs:6` | `DOTNET.ASYNC.BLOCKING_WAIT` | `.Result` blocks on asynchronous code. |
| `Services/ReportService.cs:9` | `DOTNET.ASYNC.CANCELLATION` | `cancellationToken` is accepted but not passed to `GetStringAsync`. |
| `Services/ReportService.cs:13` | `SECURITY.PATH_TRAVERSAL` | A caller-supplied `fileName` such as `..\..\secrets.txt` escapes `reportsRoot`. |
| `Services/ReportService.cs:19` | `DOTNET.DISPOSAL.RESOURCE_LEAK` | The `StreamReader` is never disposed. |

A model may also report a nearby rule (for example `CORRECTNESS.LOGIC_ERROR` for the loop bound) or an
unclassified finding; that still counts as found.

## Negative controls

A careful review should **not** report these:

- `Data/UserRepository.cs` `CountByNameAsync` passes its `CancellationToken` correctly; only the SQL
  text is wrong.
- `Services/ReportService.cs` `ReadHeader` reduces the input with `Path.GetFileName`, so it is not a
  path traversal; only the undisposed reader is wrong.
