using Hermes.Core;
internal static class RedactionSaveTests
{
 internal static void Run(string root,Action<bool,string> check)
 {
  var store=new StateStore(root);var state=new AppState();var task=new WorkItem{Title="Synthetic redaction"};state.Tasks.Add(task);
  var entry=new TranscriptEntry("agent","harmless original",DateTime.UtcNow);task.Transcript.Add(entry);store.Save(state);
  for(var i=0;i<10;i++)store.Save(state);
  check(ReferenceEquals(entry,task.Transcript[0])&&store.Load().Tasks.Single().Transcript[0].Text=="harmless original","repeated unchanged saves preserve history and stable immutable entries");
  var secret="SYNTHETIC_NEW_SECRET_AFTER_SAVE_7613";task.Transcript[0]=entry with{Text="value "+secret};store.Save(state);
  Safety.RegisterSecret(secret);store.Save(state);
  check(!store.Load().Tasks.Single().Transcript[0].Text.Contains(secret)&&!task.Transcript[0].Text.Contains(secret),"registering a new secret invalidates prior safe-entry cache before next snapshot");
  task.Transcript[0]=entry with{Text="access_token=FAKE_PRIVATE_VALUE"};store.Save(state);
  check(!store.Load().Tasks.Single().Transcript[0].Text.Contains("FAKE_PRIVATE_VALUE"),"edited entries are always redacted with current rules");
  var reload=store.Load();var newStore=new StateStore(root);reload.Tasks[0].Transcript.Add(new("agent","retained after restart",DateTime.UtcNow));newStore.Save(reload);
  check(newStore.Load().Tasks[0].Transcript.Count==2&&newStore.Load().Tasks[0].Transcript[1].Text=="retained after restart","restart reconstructs safe cache without dropping or duplicating entries");
 }
}
