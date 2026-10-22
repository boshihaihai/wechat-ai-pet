using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Text.RegularExpressions;

namespace WeChatPet.Runtime {
    public sealed class ReplyPreferencePreset {
        public string Name {get;set;}
        public string Text {get;set;}
        public override string ToString(){return Name;}
    }
    // A shortcut for editing a draft, never a persistence or inference operation.
    internal sealed class ReplyPreferencePicker {
        internal readonly ComboBox Control;
        readonly List<ReplyPreferencePreset> options;
        readonly bool group;
        bool syncing;
        internal ReplyPreferencePicker(bool group,Func<bool> canApply,Action<string> apply,Func<string> currentText=null,Action<string> reject=null) {
            this.group=group;
            options=group?GroupOptions():FriendOptions();
            options.Add(new ReplyPreferencePreset{Name="跟随聊天（默认）",Text=""});
            options.Add(new ReplyPreferencePreset{Name="自定义",Text=null});
            Control=SettingsTheme.ModelPicker();Control.Height=32;Control.Margin=new Thickness(0,5,0,1);Control.Visibility=Visibility.Collapsed;
            var label=new FrameworkElementFactory(typeof(TextBlock));label.SetBinding(TextBlock.TextProperty,new Binding("Name"));label.SetValue(TextBlock.FontSizeProperty,12.0);
            Control.ItemTemplate=new DataTemplate{VisualTree=label};Control.ItemsSource=options;Control.ToolTip=group?"选择常用风格后可继续修改，点击保存才生效。":"只调整回复方式，保留关系和背景；保存后生效。";
            AutomationProperties.SetName(Control,group?"群聊回复风格":"好友回复风格");
            Control.SelectionChanged+=delegate {
                if(syncing||Control.Visibility!=Visibility.Visible||!Control.IsEnabled||!canApply())return;
                var selected=Control.SelectedItem as ReplyPreferencePreset;
                if(selected!=null&&selected.Text!=null) {
                    string value=selected.Text;
                    if(!group&&currentText!=null) {
                        string original=currentText()??"";
                        value=WithFriendStyle(original,value);
                        if(value.Length>400){Refresh(original,true,Control.IsEnabled);if(reject!=null)reject("加入风格后超过 400 字，请先精简设定");return;}
                    }
                    apply(value);
                }
            };
        }
        internal void Refresh(string text,bool editing,bool enabled) {
            syncing=true;
            try {
                Control.Visibility=editing?Visibility.Visible:Visibility.Collapsed;Control.IsEnabled=enabled;
                string value=text??"";
                var exact=options.FirstOrDefault(o=>o.Text==value);
                if(exact==null&&!group) {
                    var styleLines=value.Replace("\r\n","\n").Split('\n').Where(IsStyleLine).ToArray();
                    if(styleLines.Length==1)exact=options.FirstOrDefault(o=>o.Text==StyleText(styleLines[0]));
                }
                Control.SelectedItem=exact??options.Last();
            }finally{syncing=false;}
        }
        static bool IsStyleLine(string line){return Regex.IsMatch(line,@"^\s*回复方式[：:]");}
        static string StyleText(string line){return Regex.Replace(line,@"^\s*回复方式[：:]\s*","").Trim();}
        static string WithFriendStyle(string original,string style) {
            string trimmed=original.Trim();
            if(trimmed.Length==0||FriendOptions().Any(o=>o.Text==trimmed))return style;
            // Only this explicitly labelled line belongs to the preset picker.
            // Arbitrary existing prose may describe a relationship or background.
            var lines=original.Replace("\r\n","\n").Split('\n');
            string background=String.Join("\n",lines.Where(line=>!IsStyleLine(line))).TrimEnd('\r','\n');
            if(style.Length==0)return background;
            return background.Length==0?style:background+"\n回复方式："+style;
        }
        static List<ReplyPreferencePreset> FriendOptions(){return new List<ReplyPreferencePreset>{
            new ReplyPreferencePreset{Name="自然简短（推荐）",Text="像平时微信聊天一样自然接话，用日常表达，先回应对方最在意的点。简单话题简短说，需要解释时再展开，不套用安慰模板或每句都追问。"},
            new ReplyPreferencePreset{Name="温和关心",Text="语气温和，关注对方这次具体说了什么。需要时表达关心，但不反复安慰、不夸大情绪，对方没求建议时先不急着给方案。"},
            new ReplyPreferencePreset{Name="轻松幽默",Text="用轻松随和的口吻接话，话题合适时可以有一点小幽默。认真或难过的话题正常回应，不硬玩梗，不默认亲密称呼。"},
            new ReplyPreferencePreset{Name="礼貌得体",Text="表达礼貌清楚，保留日常聊天的自然感，不过度客套。回应具体问题，未知信息先确认，不替我承诺时间、安排或已经完成的事。"}
        };}
        static List<ReplyPreferencePreset> GroupOptions(){return new List<ReplyPreferencePreset>{
            new ReplyPreferencePreset{Name="自然简短（推荐）",Text="自然参与当前群话题，简短接住重点，不把群消息逐条复述。需要说明时再展开，不抢话、不代表其他群成员表态。"},
            new ReplyPreferencePreset{Name="协作高效",Text="围绕当前事项直接回应，确有需要时说明待确认的问题或下一步，避免公文式套话。不虚构负责人、期限、进度或替我接下任务。"},
            new ReplyPreferencePreset{Name="轻松互动",Text="像普通群聊一样随和接话，合适时有一点幽默或表情。只回应当前讨论，不刷存在感；严肃话题保持认真。"},
            new ReplyPreferencePreset{Name="谨慎表达",Text="语气平和克制，先确认讨论中的关键信息。区分事实、意见与尚未确定的事，不替我或其他成员承诺、站队或确认完成。"}
        };}
    }
}
