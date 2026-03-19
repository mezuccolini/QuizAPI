# Release Checklist

Use this short checklist before pushing a tagged release.

## Before Push

- update [CHANGELOG.md](../CHANGELOG.md)
- update the release notes file under [docs/releases](./releases)
- run `dotnet build QuizAPI.sln -c Release`
- run `dotnet test QuizAPI.Tests\QuizAPI.Tests.csproj -c Release`
- verify [README.md](../README.md) reflects any new user-facing setup changes
- verify [DEPLOYMENT.md](../DEPLOYMENT.md) still matches production expectations
- verify rate-limit guidance still matches the configured app defaults

## Before Tagging

- confirm the working tree is clean
- confirm sample/dev-only changes are not leaking into production defaults
- confirm JWT and connection string guidance is still accurate
- confirm security logging and rate-limit behavior still match the current release
- confirm the sample package and import docs still work end to end

## Before Publishing The GitHub Release

- push `main`
- push the release tag
- paste the release notes from the matching file in [docs/releases](./releases)
- call out known limitations honestly

## Quick Smoke Test

- open `/`
- open `/swapi.html`
- verify `/health`
- log in through [manage.html](../wwwroot/manage.html)
- verify stale or non-admin sessions are rejected cleanly by [manage.html](../wwwroot/manage.html)
- upload and import a sample package
- open [quiz.html](../wwwroot/quiz.html) and verify quiz images render
