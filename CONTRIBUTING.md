# Contributing to RibatIQ

Thank you for helping improve RibatIQ.

## Getting started

```bash
dotnet build
dotnet test
```

The build treats warnings as errors and runs the .NET analyzers; the tests need no API keys and no
network access. Read [Architecture](docs/architecture.md) before changing the project structure.

## Branches and pull requests

- `main` holds releases, `dev` integrates, and `mvp` gathers the first release's work.
- Create a short-lived `feature/<topic>` branch from the branch you target and open a pull request.
- Keep pull requests focused, and include tests with every behavior change.

## Commits

Use small, coherent [Conventional Commits](https://www.conventionalcommits.org/):
`feat:`, `fix:`, `test:`, `docs:`, `build:`, `ci:`, `refactor:`, `chore:`. Every commit should build
and pass the tests on its own.

## Guidelines

- Keep Domain and Application free of vendor SDKs, Git and the CLI framework.
- Treat repository content and model output as untrusted; validate before use.
- Never log or print API keys, prompts or source code.
- Never let RibatIQ modify analyzed source files.
- Add or update tests: unit tests for Domain and Application, integration tests for adapters and
  the CLI. Provider adapters are tested against stubbed HTTP (`StubHttpHandler`) and the shared
  `LlmProviderContractTests`.

## Live provider tests

Live tests call the real APIs and are billed. They run only when you opt in:

```bash
RIBATIQ_LIVE_TESTS=1 OPENAI_API_KEY=... ANTHROPIC_API_KEY=... dotnet test
```

## Reporting security issues

Please do not open a public issue for a vulnerability; use a private security advisory on GitHub.
