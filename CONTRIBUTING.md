# Contributing to Stash.EFCore

Thank you for your interest in contributing! This guide will help you get started.

## Development Setup

### Prerequisites

- [.NET SDK 9.0](https://dotnet.microsoft.com/download) (also builds net8.0 targets)
- A C# IDE (Visual Studio, Rider, or VS Code with C# Dev Kit)

### Building

```bash
dotnet restore
dotnet build
```

### Running Tests

```bash
# All tests
dotnet test

# Single test
dotnet test --filter "FullyQualifiedName~YourTestName"

# With coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Running Benchmarks

```bash
# All benchmarks
dotnet run --project Stash.EFCore.Benchmarks -c Release

# Filtered
dotnet run --project Stash.EFCore.Benchmarks -c Release -- --filter "*KeyGeneration*"
```

## Pull Request Guidelines

1. **Fork and branch** from `main`
2. **Write tests** for new functionality
3. **Ensure all tests pass** before submitting
4. **Follow existing code style** (nullable enabled, file-scoped namespaces)
5. **Keep PRs focused** — one feature or fix per PR
6. **Update CHANGELOG.md** under `[Unreleased]`

## Code Style

- File-scoped namespaces
- Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- XML documentation on all public APIs
- `sealed` classes where inheritance is not intended
- `readonly` fields where possible

## Reporting Issues

- Use the [bug report template](.github/ISSUE_TEMPLATE/bug_report.md) for bugs
- Use the [feature request template](.github/ISSUE_TEMPLATE/feature_request.md) for ideas
- Include a minimal reproduction when reporting bugs

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
