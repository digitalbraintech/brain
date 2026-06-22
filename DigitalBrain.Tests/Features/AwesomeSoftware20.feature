Feature: Awesome Software Engineering - Software20 (new team)
  Modern neuro/LLM-assisted team. Uses local Qwen-powered neurons for self-improving simple app creation.

  Scenario: Software20 team creates a modern self-documenting task app
    Given a software20 team neuron "team20"
    When I send create simple app request "task tracker with neuro logging" for team "software20"
    Then the timeline contains a SimpleAppCreated

  Scenario: Software20 team creates a simple chat-like experience
    Given a software20 team neuron "team20-chat"
    When I send create simple app request "lightweight chat experience" for team "software20"
    Then the timeline contains a SimpleAppCreated
