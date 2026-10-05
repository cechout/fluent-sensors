# Contributing to Fluent Sensors

Thanks for wanting to contribute. Fluent Sensors is a solo hobby project, so response times may vary. For anything beyond a small fix, open an issue first to talk through the approach before writing code, saves everyone rework.

## Getting set up

1. [Fork](https://github.com/cechout/fluent-sensors/fork) the repository
2. Clone your fork

```
git clone https://github.com/<your-username>/fluent-sensors.git
```

3. Create a branch off `main`, named `feature/xxx`, `fix/xxx`, `chore/xxx`, `refactor/xxx`, or `docs/xxx`

```
git checkout -b feature/your-feature-name
```

4. Make your changes, then push and [open a pull request](https://github.com/cechout/fluent-sensors/compare) against `main`

See the "How to Build" section in the [README](https://github.com/cechout/fluent-sensors/blob/main/.github/README.md) for build prerequisites, and [AGENTS.md](https://github.com/cechout/fluent-sensors/blob/main/AGENTS.md) for the project conventions and code style.

## What we accept

* keep pull requests focused on one thing, and link the issue it addresses if there is one
* if it is a bug fix, check whether the same problem shows up elsewhere in the codebase before submitting
* avoid reformatting or restructuring code you are not otherwise touching, keep the diff to what you actually changed

## Translations

Translations run through the [Crowdin project](https://crowdin.com/project/fluent-sensors), right in the browser. Every string there comes with a note on where it shows up and what its placeholders mean, and approved translations come back into this repository as a pull request. If your language is not listed yet, open an issue and it gets added.

Please do not edit the translated files in `FluentSensors/Strings/` directly, Crowdin overwrites them with its next sync. Only `en-US` is edited in the repository, it is the source Crowdin reads from.

## A few more things

AI tools are completely fine to use for writing code. Just review what you submit and be able to explain why it is written the way it is, PRs that are clearly unreviewed AI output will not get merged.

Pull requests are squash merged, so only the pull request title ends up on `main`. Write it as one short, imperative English sentence with a prefix, like `feat: add ...` or `fix: ...`; release notes are drafted from `feat:` and `fix:` pull requests only. The commits on your branch do not need to be pristine.

