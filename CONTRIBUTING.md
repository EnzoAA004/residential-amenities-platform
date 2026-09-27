# Contributing

This project currently uses a single-maintainer workflow but follows team-grade practices.

## Workflow

1. Create or select an issue/task.
2. Create a short-lived branch from `main`.
3. Implement the smallest coherent change.
4. Update documentation when behavior, architecture, cost or operations change.
5. Add or update tests where applicable.
6. Open a Pull Request.
7. Verify CI checks.
8. Merge only when the change is coherent and documented.

## Branch naming

- `feat/<topic>`
- `fix/<topic>`
- `docs/<topic>`
- `infra/<topic>`
- `refactor/<topic>`
- `test/<topic>`

## Definition of Done

A change is done when applicable:

- acceptance criteria are met;
- tests pass;
- security implications were considered;
- operational/cost impact was considered;
- documentation is updated;
- no secrets or credentials are committed.
