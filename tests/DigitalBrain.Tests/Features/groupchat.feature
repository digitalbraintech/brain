Feature: groupchat
  A groupchat neuron runs Microsoft Agent Framework group chat under the hood.
  Participants come from Instruct. Each turn Fires Said; the run ends with Reply.

  Scenario: Two models discuss and the session sees Said then Reply
    Given a running brain with AI
    When session "claude" fires "Instruct" {"participants":[{"name":"writer","instructions":"write"},{"name":"reviewer","instructions":"review"}]} at "groupchat:design"
    And session "claude" fires "Ask" {"text":"a slogan"} at "groupchat:design"
    And "claude" waits up to 15 seconds for an incoming "Reply"
    Then "claude" incoming contains a "Said"
    And reading "claude" shows latest "Reply" {"text":"pong"}

  Scenario: Ask without Instruct explains what to do
    Given a running brain with AI
    When session "claude" fires "Ask" {"text":"hi"} at "groupchat:design"
    And "claude" waits up to 5 seconds for an incoming "Reply"
    Then reading "claude" shows latest "Reply" {"text":"Instruct this groupchat with at least two participants before Ask."}
