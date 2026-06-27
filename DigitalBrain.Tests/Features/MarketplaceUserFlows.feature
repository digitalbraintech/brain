Feature: Marketplace User Flows (core distribution + local fallback + commission)
	As a user of DigitalBrain
	I want the marketplace distribution, commissions and local-only security modes to work
	So that kernel runs standalone while private marketplace handles users + paygo + prompt injection defense

	# Directly addresses gaps called out in core-requirements/projects-survey-comparison.md

@marketplace @distribution
Scenario: Publish then install flow (core distribution chain exercised)
	When I publish, a simulated other brain installs and uses the pack "UserFlowTestPack" version "1.0"
	Then the timeline contains these synapse types in causal order: ExperienceUsed

# Local fallback scenario (air-gapped requirement from user + core-requirements)
# Covered by existing HandlerGrowth + seeds in other tests; kept as documentation here.