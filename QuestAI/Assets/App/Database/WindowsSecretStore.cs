#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace QuestAI
{
    // DPAPI CurrentUser scope. The encrypted file cannot be moved to another Windows user.
    public sealed class WindowsSecretStore : ISecretStore
    {
        [StructLayout(LayoutKind.Sequential)] struct Blob { public int length;public IntPtr data; }
        [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)]
        static extern bool CryptProtectData(ref Blob input,string description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
        [DllImport("crypt32.dll",SetLastError=true)]
        static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr pointer);
        readonly string directory;
        public WindowsSecretStore(string directory){this.directory=directory;Directory.CreateDirectory(directory);}
        string PathFor(string name){if(name!="OpenAI"&&name!="Jev")throw new ArgumentException("Unknown provider");return Path.Combine(directory,name+".bin");}
        static byte[] Transform(byte[] bytes,bool protect)
        {
            var input=new Blob{length=bytes.Length,data=Marshal.AllocHGlobal(bytes.Length)};Blob output=default;
            try
            {
                Marshal.Copy(bytes,0,input.data,bytes.Length);
                bool ok=protect?CryptProtectData(ref input,"White Room API key",IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
                if(!ok)throw new InvalidOperationException("保存キーを読み書きできません。Windowsのユーザーを確認し、キーを設定し直してください。");
                var result=new byte[output.length];Marshal.Copy(output.data,result,0,result.Length);return result;
            }
            finally
            {
                for(int i=0;i<input.length;i++)Marshal.WriteByte(input.data,i,0);Marshal.FreeHGlobal(input.data);
                if(output.data!=IntPtr.Zero){for(int i=0;i<output.length;i++)Marshal.WriteByte(output.data,i,0);LocalFree(output.data);}
                Array.Clear(bytes,0,bytes.Length);
            }
        }
        public string Get(string name)
        {
            string path=PathFor(name);if(!File.Exists(path))return "";
            var raw=Transform(File.ReadAllBytes(path),false);try{return Encoding.UTF8.GetString(raw);}finally{Array.Clear(raw,0,raw.Length);}
        }
        public void Set(string name,string value)
        {
            string path=PathFor(name);value=(value??"").Trim();if(value.Length==0){File.Delete(path);return;}
            byte[] encrypted=Transform(Encoding.UTF8.GetBytes(value),true);string temp=path+".tmp";File.WriteAllBytes(temp,encrypted);
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }
        public void DeleteAll(){foreach(string name in new[]{"OpenAI","Jev"}){File.Delete(PathFor(name));File.Delete(PathFor(name)+".tmp");}}
    }
}
#endif
