using System;
using System.Text;
using System.Text.RegularExpressions;

namespace WeChatPet.Runtime {
    // Deliberately limited to atomic, explicit statements. No semantic guesses.
    public static class PreferenceRules {
        public static string Normalize(string text) {
            return Regex.Replace(NormalizationInput(text??"").Normalize(NormalizationForm.FormKC),@"\s+","").Trim('。','!','！','.').ToLowerInvariant();
        }
        // Older clipped excerpts can contain an isolated surrogate. Windows'
        // normalizer also rejects Unicode noncharacters. Repair only the value
        // used for comparison; preserve the stored excerpt and its source.
        static string NormalizationInput(string text) {
            StringBuilder repaired=null;
            for(int i=0;i<text.Length;i++) {
                int length=1,scalar=text[i];
                if(Char.IsHighSurrogate(text[i])&&i+1<text.Length&&Char.IsLowSurrogate(text[i+1])){scalar=Char.ConvertToUtf32(text[i],text[i+1]);length=2;}
                bool invalid=length==1&&Char.IsSurrogate(text[i])||scalar>=0xfdd0&&scalar<=0xfdef||(scalar&0xffff)>=0xfffe;
                if(invalid){if(repaired==null){repaired=new StringBuilder(text.Length);repaired.Append(text,0,i);}repaired.Append('\ufffd');}
                else if(repaired!=null)repaired.Append(text,i,length);
                i+=length-1;
            }
            return repaired==null?text:repaired.ToString();
        }
        public static string Key(string text,bool manual) {
            if(Regex.IsMatch(text??"",@"[\r\n]"))return null;
            string value=Normalize(text);
            if(manual)value=Regex.Replace(value,@"^(饮食偏好|饮品偏好|偏好|称呼)[:：]","");
            // An explicit past/current correction can be reduced to its current clause.
            var correction=Regex.Match(value,@"^我(?:以前|之前|原来)([^,，]+)[,，](?:我)?现在(.+)$");
            if(correction.Success) {
                string past=correction.Groups[1].Value,current=correction.Groups[2].Value;
                if(Key("我"+past,false)==null)return null;
                var omitted=Regex.Match(current,@"^(不再|不|开始|可以|能)?(吃|喝|喜欢|习惯)了$");
                var previous=Regex.Match(past,@"^(?:不再|不|从不)?(吃|喝|喜欢|习惯)(.+?)(?:了)?$");
                if(omitted.Success&&previous.Success&&omitted.Groups[2].Value==previous.Groups[1].Value)
                    current=omitted.Groups[1].Value+omitted.Groups[2].Value+previous.Groups[2].Value;
                value="我现在"+current;
            }
            if(value.Length==0||value.Length>120||Regex.IsMatch(value,@"[?？“”「」,，;；:：]|如果|假如|开玩笑|哈哈|别人|他说|她说|可能|也许|大概|应该|好像|今天|这次|今晚|明天|暂时|偶尔|吗|呢|吧|但是|不过|而且|或者|还是|除了|除非|只有|虽然|但|因为|所以|并不|不一定|不是|以前|之前|原来"))return null;
            // Questions often arrive without punctuation. An unknown object or
            // a completed eating/drinking state is not a lasting preference.
            if(Regex.IsMatch(value,@"什么|啥|怎么|怎样|如何|哪种|哪个|哪些|哪里|哪儿|是否"))return null;
            var name=Regex.Match(value,@"^(?:以后|今后|现在)?(?:请)?(?:不要再|别再|别|不要)?叫我(.{1,16})$");
            if(name.Success&&!Regex.IsMatch(name.Groups[1].Value,@"[和与、]|或者|还是"))return "name";
            if(!manual&&!value.StartsWith("我",StringComparison.Ordinal))return null;
            value=Regex.Replace(value,@"^我","");
            value=Regex.Replace(value,@"^(?:现在|以后|今后|已经|最近)","");
            if(Regex.IsMatch(value,@"^(?:吃|喝)(?:饱|撑|醉|完|光|够|过|着|好了)"))return null;
            // Intensity does not change the preference's subject or topic.
            // Accept only this explicit word order; do not guess "不太喜欢".
            value=Regex.Replace(value,@"^(?:很|特别|比较)(?=(?:不)?喜欢)","");
            var match=Regex.Match(value,@"^(?:不再|不|从不|从来不|再也不|开始|改为|改成|可以|能)?(喜欢|讨厌|爱|吃|喝|习惯)(.{1,32}?)(?:了)?$");
            if(!match.Success)return null;
            string verb=match.Groups[1].Value,target=match.Groups[2].Value;
            // "喜欢吃香菜" belongs to the eating topic, while plain "喜欢香菜"
            // remains a separate liking claim. The original sentence is kept
            // as evidence; only its conflict key is normalized here.
            if(verb=="喜欢"&&(target.StartsWith("吃",StringComparison.Ordinal)||target.StartsWith("喝",StringComparison.Ordinal))){verb=target.Substring(0,1);target=target.Substring(1);}
            if(target.Length==0)return null;
            if(Regex.IsMatch(target,@"[和与、]|一起|一下|一点|一次|只|都|也|还|很|更|最|不|吃|喝")&&verb!="喜欢"&&verb!="讨厌"&&verb!="爱")return null;
            if(target=="了"||Regex.IsMatch(target,@"[和与、]|但是|不|一下|今天|明天|想|你|他|她|我们|这个|那个"))return null;
            return (verb=="爱"||verb=="讨厌"?"喜欢":verb)+":"+target;
        }
    }
}
