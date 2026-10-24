using System.Linq;
namespace QuestAI
{
    public static class ClipboardInput
    {
        public static bool TryNormalize(string text,bool secret,out string value)
        {
            value=(text??"").Trim();
            if(value.Length==0||value.Length>4096||value.Any(char.IsControl)||(secret&&value.Any(char.IsWhiteSpace))){value="";return false;}
            return true;
        }
    }
}
