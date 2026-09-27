# Business Rules

These rules are the current v0.1 model. Items marked **TBD** require stakeholder validation.

| ID | Rule | Status |
| --- | --- | --- |
| RB-001 | Event reservations require the SUM as the base resource. | Proposed |
| RB-002 | Pool and barbecue/grill are independent optional event add-ons with their own price rules. | Proposed |
| RB-003 | Shared-leisure SUM reservations may coexist only with compatible shared-leisure reservations. | Proposed |
| RB-004 | A user creating/joining a shared-leisure window must be informed that the space may be shared. | Proposed |
| RB-005 | Exclusive leisure blocks incompatible SUM use during its time range. | Proposed |
| RB-006 | Event reservations block incompatible SUM use during the complete configured event window. | Proposed |
| RB-007 | Prices are configuration data, not constants embedded in the client. | Accepted |
| RB-008 | A reservation stores a price snapshot/line-item result so later price changes do not rewrite historical charges. | Accepted |
| RB-009 | A pending-payment reservation may hold required resources only until its configured expiration time. | Accepted |
| RB-010 | Expired unpaid reservations release held resources. | Accepted |
| RB-011 | A Mercado Pago redirect/client callback alone does not authorize final payment confirmation; final state is established by backend verification/provider notification. | Accepted design rule |
| RB-012 | Cash payment is not considered received until an authorized person confirms it. | Proposed |
| RB-013 | Financially relevant reservations/payments are cancelled/voided through state transitions rather than silently hard-deleted. | Accepted |
| RB-014 | Administrative reservation changes must record actor, timestamp and reason where applicable. | Accepted |
| RB-015 | Building/unit membership must be authorized; public selection of an arbitrary unit is not sufficient for account creation. | Accepted |
| RB-016 | Exact event windows, prices, cancellation rules, cleaning rules and shared capacity remain configurable/TBD until validated. | TBD |
