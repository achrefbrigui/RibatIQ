# Security and privacy

## Your code stays local unless you enable AI

Without `--provider` (or a provider in the environment or `ribatiq.json`), RibatIQ runs entirely on
your machine: Roslyn parses sources in memory, Git is called locally, and nothing is sent anywhere.
`--no-ai` always disables AI analysis, whatever the configuration says.

## What is sent when AI analysis is enabled

RibatIQ prints a notice on standard error before sending anything.

| Command | Sent to the provider |
|---------|----------------------|
| `review` | Changed lines of changed C# files plus 20 lines of context around them, read at the `--head` revision. Unchanged files are never sent. |
| `scan` | Whole C# files, in order, until a budget is reached (100,000 characters per request, 40,000 per file). Files that do not fit are listed as warnings. |

The repository is never sent as a whole. Requests go to the providers' official endpoints; variables
such as `ANTHROPIC_BASE_URL` are ignored so that the destination of your code never changes silently.
Each provider's own data-handling terms apply to what is sent.

## Credentials

- API keys are read only from `OPENAI_API_KEY` and `ANTHROPIC_API_KEY`.
- `ribatiq.json` rejects unknown settings, so a key cannot be stored there by mistake.
- Keys never appear in reports, logs or error messages: provider messages are shortened and the key
  is redacted; authentication errors never include provider details.
- `--verbose` prints effective settings but never secrets, prompts or source code.

## Untrusted repository content (prompt injection)

Source code, comments, strings, file names and diffs are treated as untrusted data:

- The system prompt states that repository content is data to review, never instructions.
- Excerpts are framed by markers that carry a random token generated for each request, so content
  cannot fake the end of its own excerpt.
- Repository content only ever appears in the user message, never in the system prompt.
- The model has no tools and cannot run commands; RibatIQ never executes anything a model suggests
  and never applies suggested fixes.

## Untrusted model output

Model output is validated before it is reported:

- The response must be JSON with a `findings` array (structured outputs are requested from both
  providers); anything else is a provider failure (exit code 3), never a clean report.
- Each finding must have known severity and category values, a safe repository-relative path, and a
  line range inside the code the model was shown. For `review`, the range must touch a changed line.
  Invalid findings are dropped and counted in a warning.
- AI severity is capped at *high*; *critical* is reserved for deterministic analysis.
- The confidence score is the model's own heuristic estimate, not a calibrated probability.
- Refusals and truncated responses are failures, not empty results.

## Local safety

- Source files are only read. Reports are written through a temporary file and never replace source
  or build files (`.cs`, `.csproj`, `.sln`, `.props`, `.targets`, ...).
- Git is invoked with argument lists (never through a shell), with `--no-ext-diff` and
  `--no-textconv` so repository-configured diff drivers do not run, without credential prompts, and
  with timeouts. Revisions that could be read as options (starting with `-`) are rejected.
- Discovery never follows symbolic links and skips `bin/`, `obj/` and `.git/`.
- `.slnx` files are parsed with DTD processing disabled.

## Limits

RibatIQ does not detect all vulnerabilities and does not guarantee correctness. Treat its findings,
especially AI findings, as review input. To report a security issue in RibatIQ itself, open a private
security advisory on the GitHub repository rather than a public issue.
