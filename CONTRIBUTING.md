# Contributing

Thanks for taking a look. Bug reports, ideas and pull requests are welcome.

## Before you start

- For anything larger than a small fix, open an issue first so the direction can be agreed.
- Security problems go through the private report described in [SECURITY.md](SECURITY.md), not a public issue.

## Working on the code

```bash
dotnet test Shabpa.slnx          # build and run the tests
dotnet run --project src/Shabpa -- --demo --dry-run
```

- `--demo` uses sample data and `--dry-run` only simulates power commands, so it is safe to try things.
- `src/Shabpa/Core` holds logic that can be tested without a window; keep new logic there and keep pages thin.
- Everything that is shown to the user needs both Persian and English text (`L.T("فارسی", "English")`).
- Anything that deletes, schedules or sends data needs an explicit confirmation and a test.

## Pull requests

- Keep a change focused and add a test for new behaviour.
- Add a line for user-visible changes to [CHANGELOG.md](CHANGELOG.md) under a new *Unreleased* heading.
- Follow the style of the surrounding code.
