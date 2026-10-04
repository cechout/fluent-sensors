# Contributing to FluentSensors

Thanks for wanting to contribute. FluentSensors is a solo hobby project, so response times may vary. For anything beyond a small fix, open an issue first to talk through the approach before writing code, saves everyone rework.

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

The app text lives in `FluentSensors/Strings/<language>/Resources.resw`, with `en-US` as the source. To fix or add a translation, edit the file of that language, or copy the `en-US` file into a new folder named after the language tag (`fr-FR`, `pl-PL`, ...) and translate the values. Keys without a translation fall back to English, so a partial file is fine.

* never translate sensor names, hardware type names or the CSV column headers, they come from LibreHardwareMonitor and other tools rely on them
* keep placeholders like `{0}` and line breaks as they are
* keys ending in `_One`, `_Few` and `_Many` are count forms: one, two to four, and everything else; a language without a separate few form repeats the many text
* a new language also needs one line in `AppLanguage.Supported` (`FluentSensors/Common/Localization/AppLanguage.cs`) to show up in the language setting

## A few more things

AI tools are completely fine to use for writing code. Just review what you submit and be able to explain why it is written the way it is, PRs that are clearly unreviewed AI output will not get merged.

Pull requests are squash merged, so only the pull request title ends up on `main`. Write it as one short, imperative English sentence with a prefix, like `feat: add ...` or `fix: ...`; release notes are drafted from `feat:` and `fix:` pull requests only. The commits on your branch do not need to be pristine.

