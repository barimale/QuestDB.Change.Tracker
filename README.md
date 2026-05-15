# QuestDB.Change.Tracker.Api [POC]
## Usage
It is meant as a detector running in a background service. 
Put the notification logic to the OnChange event to have the information about change propagated through 
the entire system i.e.:
```
var tracker = new TrackChangesEngine(connectionFactory);
tracker.OnChange += async (args) =>
{
    // inform via hub etc...
    // save to DB etc...
};
```
## Co-author
 - GitHub Copilot