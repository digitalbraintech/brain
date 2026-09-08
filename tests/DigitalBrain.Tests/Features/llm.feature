Feature: llm
  An llm neuron Receives Ask, calls IChatClient, and Fires Reply at the Session.
  Unknown types are journaled and ignored. Instruct is the optional system prompt.

  Scenario: Ask is answered with a Reply
    Given a running brain with AI
    When session "claude" fires "Ask" {"text":"hello"} at "llm:ino"
    And "claude" waits up to 5 seconds for an incoming "Reply"
    Then reading "claude" shows latest "Reply" {"text":"pong"}

  Scenario: Instruct is used as the system prompt
    Given a running brain with AI
    When session "claude" fires "Instruct" {"text":"be terse"} at "llm:ino"
    And session "claude" fires "Ask" {"text":"hello"} at "llm:ino"
    And "claude" waits up to 5 seconds for an incoming "Reply"
    Then reading "claude" shows latest "Reply" {"text":"pong"}

  Scenario: An unknown signal is journaled and ignored
    Given a running brain with AI
    When session "claude" fires "Ping" {} at "llm:ino"
    Then reading "llm:ino" shows latest "Ping" {}
    And "claude" outgoing journal has 1 entries
    And "claude" incoming journal is empty
