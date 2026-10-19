# docs/wiki

The source of the project's [GitHub wiki](https://github.com/YD359Team/ZeppelinForms-solution/wiki).

- One page per file; the file name is the page name (`Getting-Started.md` → *Getting Started*).
- Links between pages use the page name without `.md`: `[Layout](Layout)`. They work in
  the wiki, not when the files are browsed in the repository.
- `_Sidebar.md` and `_Footer.md` are shown around every page.
- `.github/workflows/wiki.yml` publishes the folder to the wiki on every push to `master`
  that touches it, and on demand. Edits made in the wiki itself are overwritten: change
  the files here.
- This README is not published.

The wiki must exist before the first publish: create any page once in the GitHub UI
(Wiki → Create the first page), then run the workflow.
