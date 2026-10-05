# DevOps Strategy

## Objectives

- Reproducible local and cloud environments.
- Automated quality checks.
- Controlled deployments.
- Infrastructure changes reviewed as code.
- Observable and cost-aware production operation.

## Target flow

```mermaid
flowchart LR
    Dev[Developer]
    Branch[Short-lived branch]
    PR[Pull Request]
    CI[CI: build/test/scan]
    Main[main]
    Stage[Staging]
    Approval[Production approval]
    Prod[Production]

    Dev --> Branch --> PR --> CI
    CI --> Main
    Main --> Stage
    Stage --> Approval --> Prod
```

## Tooling direction

- GitHub Actions: CI/CD orchestration.
- Docker: reproducible application packaging.
- Terraform: Azure IaC.
- Azure-managed runtime: selected after cost/technical spike.
- Puppet: host configuration only where a VM/host actually exists and justifies configuration management.

## Environment strategy

- Local: Docker-based dependencies where useful.
- Staging: integration/acceptance environment.
- Production: real building use.

Do not represent environments with permanent `dev`, `staging` and `production` Git branches.

## Delivery status

Implemented now:

1. restore dependencies;
2. compile backend;
3. backend unit/integration tests;
4. build client;
5. client tests;
6. production API/client container builds;
7. Terraform format/init/validate without cloud credentials;
8. manual Azure OIDC workflow for Terraform plan/apply;
9. ACR image builds and Container App revision updates after an approved apply;
10. public client + proxied API smoke check.

Still to add after the first real staging deployment:

- dependency/container security scanning;
- Azure budget/alerts;
- environment-specific payment secrets;
- a production migration strategy that does not rely on startup migrations;
- gated production deployment after staging acceptance.
