# Refund Handling Process

Eligibility:
- Customer must have purchased within last 30 days.
- Must provide order ID or receipt.
- Item must be unused and in original packaging for standard refunds.
- Digital products are non-refundable after download.

Decision flow:
1. Receive RefundRequested with customerId, orderId, amount, reason, daysSincePurchase.
2. If daysSincePurchase > 30: deny with "outside window".
3. If no receipt and not loyalty member: require additional verification.
4. If reason is "defective" and within 14 days: auto-approve full refund + shipping credit.
5. Otherwise: escalate to agent for manual review if amount > 500 or suspicious.

Outcomes to emit:
- RefundApproved { RequestId, ApprovedAmount, ReasonCode }
- RefundDenied { RequestId, DenialReason }
- RefundExecuted { RequestId, TransactionRef }

Side effects via emitted commands (handled outside pack):
- IssueCredit
- NotifyCustomer
- LogAudit

Keep all decisions auditable via emitted PackEmission and outcome synapses with full causation chain.