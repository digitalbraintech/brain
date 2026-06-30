Feature: Pack config form on install (Slice 2 - config-driven experience)
	As a user installing a pack that declares RequiredConfig
	I want the kernel to render an in-app config form on install
	So that submitting it persists my values (encrypted) without leaving the app

	@distribution @config
	Scenario: Installing a pack with RequiredConfig emits a config form and submit persists values
		Given a generic pack "GenericConfiguredPack" declaring 3 required config fields
		When I publish and install the pack
		Then a config form surface is emitted whose tree contains the fields "telegram_token", "llm_provider", "llm_key"
		When I submit configuration for the pack with token "tok-123", provider "openai", key "sk-secret"
		Then the pack config store returns token "tok-123", provider "openai", key "sk-secret"

	@distribution @e2e
	Scenario: Full reactive loop - an inbound message round-trips through the pack and the stubbed LLM to an egress reply
		Given the Telegram responder experience is installed
		Then the install emits a config form whose tree contains the fields "telegram_token", "llm_provider", "llm_key"
		When I provide the Telegram configuration token "tok-123", provider "openai", key "sk-secret"
		And the LLM responder is active and the egress bus is watching "TelegramReplyRequested"
		And a Telegram message arrives for chat 7 with text "hi"
		Then the embodied pack emits an AskLlm for "hi"
		And a "TelegramReplyRequested" reply for chat 7 with text "ANSWER:hi" reaches the egress bus

	@distribution @e2e @n1
	Scenario: N+1 reactivity - two packs both react to one broadcast with no restart
		Given both the Telegram responder and the keyword watcher are installed
		And I provide the Telegram configuration token "tok-123", provider "openai", key "sk-secret"
		And the LLM responder is active and the egress bus is watching "TelegramReplyRequested" and "ReminderScheduled"
		When a Telegram message with text "remind me to call mom" is ingested for chat 7
		Then a "TelegramReplyRequested" reply for chat 7 reaches the egress bus
		And a "ReminderScheduled" signal for chat 7 reaches the egress bus
