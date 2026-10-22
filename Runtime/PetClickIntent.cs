using System;
using System.Globalization;
using System.Text;

namespace WeChatPet.Runtime {
    // Pure gesture policy: a click becomes an intent only after the double-click
    // window has closed. It never performs input or reads the desktop.
    internal sealed class PetClickIntent {
        bool pressed,pending;
        double startX,startY;
        long due;
        string token;
        public bool IsPressed {get{return pressed;}}
        public void Begin(double x,double y,string contextToken) {
            Cancel();pressed=true;startX=x;startY=y;token=contextToken;
        }
        public bool Move(double x,double y,double thresholdX,double thresholdY) {
            if(!pressed)return false;
            if(Math.Abs(x-startX)<Math.Max(1,thresholdX)&&Math.Abs(y-startY)<Math.Max(1,thresholdY))return false;
            Cancel();return true;
        }
        public bool Release(long now,int doubleClickMilliseconds) {
            if(!pressed)return false;
            pressed=false;
            if(String.IsNullOrEmpty(token)){Cancel();return false;}
            pending=true;due=now+Math.Max(1,doubleClickMilliseconds);return true;
        }
        public string Take(long now) {
            if(!pending||now<due)return null;
            string result=token;Cancel();return result;
        }
        public void Cancel() {pressed=pending=false;token=null;due=0;}
        public static string ContextToken(ChatSnapshot s) {
            if(s==null||!s.Available||!s.GroupChat||String.IsNullOrEmpty(s.AccountId)||String.IsNullOrEmpty(s.GroupId))return null;
            var b=new StringBuilder();
            foreach(string value in new[]{s.AccountId,s.GroupId,s.Session,s.Tail,s.ContentRevision,s.InputIdentity,s.ViewIdentity,
                s.Activity.ToString(CultureInfo.InvariantCulture),s.Window.ToString(CultureInfo.InvariantCulture)}) {
                string v=value??"";b.Append(v.Length).Append(':').Append(v);
            }
            return b.ToString();
        }
    }
}
