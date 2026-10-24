using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace QuestAI
{
    [Serializable] public sealed class CompanionJourney
    {
        public int conversations;
        public bool waved, memoryGame;
        public List<string> rooms=new List<string>();
        public static CompanionJourney Load(IMemoryRepository repository,string character)
            =>JsonConvert.DeserializeObject<CompanionJourney>(repository.Load("journey:"+character)??"null")??new CompanionJourney();
        public void Save(IMemoryRepository repository,string character)=>repository.Save("journey:"+character,JsonConvert.SerializeObject(this));
        public void Visit(string room){if(!string.IsNullOrEmpty(room)&&!rooms.Contains(room))rooms.Add(room);}
        public int Stamps=>(conversations>=1?1:0)+(conversations>=5?1:0)+(waved?1:0)+(rooms.Count>=3?1:0)+(memoryGame?1:0);
        public string Summary()=>"交流スタンプ  "+Stamps+" / 5\n"+
            Mark(conversations>=1)+" はじめての会話\n"+Mark(conversations>=5)+" 会話を5回（"+Math.Min(conversations,5)+"/5）\n"+
            Mark(waved)+" 手を振ってごあいさつ\n"+Mark(rooms.Count>=3)+" 3つの部屋を訪問（"+Math.Min(rooms.Count,3)+"/3）\n"+Mark(memoryGame)+" しぐさクイズで3問正解";
        static string Mark(bool done)=>done?"★":"☆";
        public static readonly string[] Topics={"もし一日だけ空を飛べたら、どこへ行く？","最近ちょっと嬉しかったことを話そう。","ふたりでカフェを開くなら、どんなメニュー？","子どもの頃に好きだった遊びは？","この部屋で過ごす理想の休日を考えよう。","架空の旅先をひとつずつ考えて、旅行の計画を作ろう。"};
    }
}
