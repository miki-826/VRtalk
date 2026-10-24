using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace QuestAI
{
    public class LocalRepository : IMemoryRepository
    {
        [Serializable] class Data { public List<MessageRecord> messages = new List<MessageRecord>(); public List<MemoryRecord> memories = new List<MemoryRecord>(); public Dictionary<string,string> values = new Dictionary<string,string>(); }
        Data data = new Data(); readonly string path;
        public LocalRepository(string path = null) { this.path = path; if (path != null && File.Exists(path)) data = JsonConvert.DeserializeObject<Data>(File.ReadAllText(path)) ?? new Data(); }
        void Flush() { if (path == null) return; var temp = path + ".tmp"; File.WriteAllText(temp,JsonConvert.SerializeObject(data)); if (File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path); }
        public void AddMessage(MessageRecord message) { if (!data.messages.Any(x => x.id == message.id)) data.messages.Add(message); Flush(); }
        public IReadOnlyList<MessageRecord> Recent(string id, int limit) => data.messages.Where(x=>x.characterId==id).OrderByDescending(x=>x.createdAt).ThenByDescending(x=>data.messages.IndexOf(x)).Take(limit).Reverse().ToArray();
        public IReadOnlyList<MemoryRecord> Search(string id,string text,int limit) => MemoryRanking.Rank(data.memories,id,text,limit);
        public void AddMemories(IEnumerable<MemoryRecord> records) { foreach(var r in records) if (!data.memories.Any(x=>x.characterId==r.characterId && x.content==r.content)) data.memories.Add(r); Flush(); }
        public void Save(string key,string json) { data.values[key]=json; Flush(); }
        public string Load(string key) => data.values.TryGetValue(key,out var value)?value:null;
        public void DeleteMemories(string id) { data.memories.RemoveAll(x=>x.characterId==id); data.messages.RemoveAll(x=>x.characterId==id); Flush(); }
        public void DeleteAll() { data=new Data(); Flush(); }
        public void Dispose() { Flush(); }
    }
    public class SessionSecrets : ISecretStore
    {
        readonly Dictionary<string,string> values = new Dictionary<string,string>();
        public string Get(string name) => values.TryGetValue(name,out var value)?value:"";
        public void Set(string name,string value) { values[name]=value.Trim(); }
        public void DeleteAll() => values.Clear();
    }
    public sealed class AndroidStorage : IMemoryRepository, ISecretStore
    {
        readonly AndroidJavaObject bridge;
        public AndroidStorage()
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            bridge = new AndroidJavaObject("com.vibecodingclub.questai.LocalStorage",activity);
        }
        public void AddMessage(MessageRecord m) => bridge.Call("addMessage",JsonConvert.SerializeObject(m));
        public IReadOnlyList<MessageRecord> Recent(string id,int limit) => JsonConvert.DeserializeObject<List<MessageRecord>>(bridge.Call<string>("recent",id,limit));
        public IReadOnlyList<MemoryRecord> Search(string id,string text,int limit)
        {
            var candidates=JsonConvert.DeserializeObject<List<MemoryRecord>>(bridge.Call<string>("memories",id));
            var selected=MemoryRanking.Rank(candidates,id,text,limit);
            foreach(var m in selected) bridge.Call("touchMemory",m.id,DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return selected;
        }
        public void AddMemories(IEnumerable<MemoryRecord> records) => bridge.Call("addMemories",JsonConvert.SerializeObject(records));
        public void Save(string key,string json) => bridge.Call("save",key,json);
        public string Load(string key) => bridge.Call<string>("load",key);
        public void DeleteMemories(string id) => bridge.Call("deleteMemories",id);
        public void DeleteAll() => bridge.Call("deleteAllData");
        string ISecretStore.Get(string name) => bridge.Call<string>("getSecret",name);
        void ISecretStore.Set(string name,string value) => bridge.Call("setSecret",name,value.Trim());
        void ISecretStore.DeleteAll() => bridge.Call("deleteSecrets");
        public void Dispose() { bridge.Call("close"); bridge.Dispose(); }
    }
}
