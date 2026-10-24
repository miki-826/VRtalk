using UnityEngine;

namespace QuestAI
{
    // Ambient sounds synthesised at startup, so no recorded audio assets (or their licences) are needed.
    public static class ProceduralAudio
    {
        const int Rate=44100;
        static AudioClip wind,crackle;static AudioClip[] chirps;

        // Seamless loop: the tail is cross-faded into the head.
        static AudioClip Loop(string name,float[] data,int fade)
        {
            int n=data.Length-fade;var loop=new float[n];
            for(int i=0;i<n;i++){float f=i<fade?(float)i/fade:1;loop[i]=data[i]*f+(i<fade?data[n+i]*(1-f):0);}
            var clip=AudioClip.Create(name,n,1,Rate,false);clip.SetData(loop,0);return clip;
        }
        // Outdoor wind heard through glass: low-passed brown noise with slow swells and a faint whistle band.
        public static AudioClip Wind()
        {
            if(wind)return wind;
            var rng=new System.Random(11);int n=Rate*12;var d=new float[n];float brown=0,low=0,band=0,bandLow=0;
            for(int i=0;i<n;i++)
            {
                float white=(float)rng.NextDouble()*2-1;brown=Mathf.Clamp(brown+white*.02f,-1,1)*.998f;low+=(brown-low)*.08f;
                bandLow+=(white-bandLow)*.25f;band+=(bandLow-band)*.05f;
                float t=(float)i/Rate;float swell=.55f+.3f*Mathf.Sin(t*.7f)+.15f*Mathf.Sin(t*1.9f+1);
                d[i]=(low*1.6f+(bandLow-band)*.12f*swell)*swell;
            }
            return wind=Loop("Wind",d,Rate);
        }
        // Wood fire: soft roar plus random pops and crackles of varying size.
        public static AudioClip Crackle()
        {
            if(crackle)return crackle;
            var rng=new System.Random(23);int n=Rate*8;var d=new float[n];float low=0;
            for(int i=0;i<n;i++){float white=(float)rng.NextDouble()*2-1;low+=(white-low)*.03f;d[i]=low*.25f;}
            for(int k=0;k<70;k++)
            {
                int start=rng.Next(n);float size=(float)rng.NextDouble();int len=(int)(Rate*(.003f+.02f*size));float amp=.15f+.6f*size*size;float hp=0;
                for(int j=0;j<len&&start+j<n;j++){float white=(float)rng.NextDouble()*2-1;hp=white-hp*.5f;d[start+j]+=hp*amp*Mathf.Exp(-j*6f/len);}
            }
            return crackle=Loop("Fire crackle",d,Rate/2);
        }
        // Songbird phrases: short FM sweeps and trills with natural pitch scatter.
        public static AudioClip Chirp(int variant)
        {
            if(chirps==null)chirps=new AudioClip[6];
            if(chirps[variant])return chirps[variant];
            var rng=new System.Random(100+variant);int notes=2+rng.Next(5);float gap=.05f+(float)rng.NextDouble()*.1f;
            float baseFreq=2600+(float)rng.NextDouble()*1800;bool trill=variant%3==2;
            int n=(int)(Rate*(notes*(.09f+gap)+.2f));var d=new float[n];float phase=0;
            for(int k=0;k<notes;k++)
            {
                int start=(int)(Rate*k*(.09f+gap));float dur=trill?.035f:.06f+(float)rng.NextDouble()*.07f;int len=(int)(Rate*dur);
                float f0=baseFreq*(.85f+(float)rng.NextDouble()*.3f),f1=f0*(trill?1.05f:.6f+(float)rng.NextDouble()*.9f);
                for(int j=0;j<len&&start+j<n;j++)
                {
                    float x=(float)j/len;float f=Mathf.Lerp(f0,f1,x)+Mathf.Sin(x*40)*120;phase+=2*Mathf.PI*f/Rate;
                    float env=Mathf.Sin(Mathf.PI*x);env*=env;d[start+j]+=Mathf.Sin(phase)*env*.35f+Mathf.Sin(phase*2)*env*.06f;
                }
                if(trill&&k==notes-1)notes=Mathf.Min(notes+1,9);
            }
            var clip=AudioClip.Create("Chirp "+variant,n,1,Rate,false);clip.SetData(d,0);return chirps[variant]=clip;
        }
    }
}
