# Cost Model

Actual Azure prices are intentionally not hard-coded until the exact region, services and SKUs are selected and verified.

## Cost drivers to track

- web hosting/runtime;
- backend compute;
- managed PostgreSQL compute/storage/backups;
- container registry/storage;
- secret management;
- monitoring/log ingestion/retention;
- outbound network traffic;
- notification provider usage;
- domain/DNS;
- Mercado Pago transaction fees;
- optional VM cost if a Puppet/host-management lab ever becomes production-relevant (not currently planned).

## Scenarios

### Scenario A — Pilot
- 1 building
- 10 units
- very low booking traffic

### Scenario B — Small SaaS
- multiple buildings
- hundreds of residents
- shared application runtime

### Scenario C — Growth
- larger tenant count
- stronger isolation/observability/support requirements

## Metrics

Track at minimum:

- monthly platform infrastructure cost;
- monthly payment-provider variable fees;
- cost per active building;
- cost per active resident;
- cost per successful reservation;
- database/storage growth;
- log/telemetry cost.

## Next step

After Azure service selection, create a dated cost estimate and record assumptions such as region, SKU, hours, scale-to-zero behavior, storage and backup retention.
