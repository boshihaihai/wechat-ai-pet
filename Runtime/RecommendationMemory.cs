using System;
using System.Collections.Generic;

namespace WeChatPet.Runtime {
    // Capture values before dispatch. Group and private memory never share a field.
    internal static class RecommendationMemory {
        internal static Dictionary<string,object> Fields(MemoryStore store,ChatSnapshot snapshot,long now) {
            bool group=snapshot!=null&&snapshot.GroupChat;
            var fields=new Dictionary<string,object>{{"contextMode",group?"group":"private"}};
            if(group) {
                var context=store.GroupContext(snapshot);
                fields.Add("memory","");fields.Add("styleExamples",new string[0]);
                fields.Add("groupMemory",new Dictionary<string,object>{{"replyPreferences",context.ReplyPreferences},{"agreements",context.Agreements}});
            } else {
                var profile=store.Profile(snapshot,now);
                fields.Add("friendProfile",new Dictionary<string,object>{{"replyPreferences",profile.ReplyPreferences},{"friendNotes",profile.FriendNotes.ToArray()},{"selfNotes",profile.SelfNotes.ToArray()}});
                fields.Add("memory",store.Context(snapshot,now,false));fields.Add("styleExamples",store.ExpressionExamples(snapshot));
            }
            return fields;
        }
    }
}
