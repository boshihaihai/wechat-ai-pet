using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
public static partial class NativeMessageReader {
 static string Dll;
 static string Exe; public static void Configure(string exe,string dll){Exe=exe;Dll=dll;}
 const string Hash="10f8e995453e2da46d4f2b5080cd6da1f13cc5147746adc119ceae38cb039de5";
 const long RootRva=0xB930890,CacheVtable=0x91466D8,InterfaceVtable=0x8EBA048,ObjectVtable=0x920EC28;
 const int MaxChain=128,MaxEntries=1000000;
 const long MaxPointer=0x00007FFFFFFFFFFF;
 [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint a,bool i,int p);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool CloseHandle(IntPtr h);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool ReadProcessMemory(IntPtr p,IntPtr a,byte[] b,UIntPtr n,out UIntPtr r);
 [DllImport("kernel32.dll",SetLastError=true)]static extern UIntPtr VirtualQueryEx(IntPtr p,IntPtr a,out Mbi b,UIntPtr n);
 [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)]static extern bool QueryFullProcessImageName(IntPtr p,uint f,StringBuilder b,ref int n);
 [DllImport("psapi.dll",SetLastError=true)]static extern bool EnumProcessModulesEx(IntPtr p,IntPtr[] m,uint n,out uint needed,uint f);
 [DllImport("psapi.dll",SetLastError=true,CharSet=CharSet.Unicode)]static extern uint GetModuleFileNameEx(IntPtr p,IntPtr m,StringBuilder b,int n);
 [DllImport("psapi.dll",SetLastError=true)]static extern bool GetModuleInformation(IntPtr p,IntPtr m,out ModuleInfo i,uint n);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(IntPtr p,out FileTime c,out FileTime e,out FileTime k,out FileTime u);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetExitCodeProcess(IntPtr p,out uint code);
 [StructLayout(LayoutKind.Sequential)]struct Mbi{public ulong Base,Allocation;public uint AllocationProtect,Pad1;public ulong Size;public uint State,Protect,Type,Pad2;}
 [StructLayout(LayoutKind.Sequential)]struct ModuleInfo{public IntPtr Base;public uint Size;public IntPtr Entry;}
 [StructLayout(LayoutKind.Sequential)]struct FileTime{public uint Low,High;public ulong Value{get{return ((ulong)High<<32)|Low;}}}
 sealed class Failure:Exception{public Failure(string s):base(s){}}
 sealed class Binding {
  public long Cache,Table,Buckets,Node,Interface,Private,Control,Object;
  public uint Size,BucketCount,Seed,Bucket;public int RefCount,Visited;public string Trace;
  public bool Same(Binding b){return b!=null&&Cache==b.Cache&&Table==b.Table&&Buckets==b.Buckets&&Node==b.Node&&
   Interface==b.Interface&&Private==b.Private&&Control==b.Control&&Object==b.Object&&Size==b.Size&&
   BucketCount==b.BucketCount&&Seed==b.Seed&&Bucket==b.Bucket&&RefCount==b.RefCount&&Trace==b.Trace;}
 }
  static byte[] tagKey;
 public static WeChatPet.Runtime.MemoryIdentity MemoryIds;
 static string Tag(string text){using(var h=new HMACSHA256(tagKey))return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
 static string SmallString(IntPtr p,long a){
  byte[] header=Read(p,a,32);long size=BitConverter.ToInt64(header,16),capacity=BitConverter.ToInt64(header,24);
  Need(size>=0&&size<=512&&capacity>=size&&capacity<=1048576,"identifier_string_bounds");
  if(size==0)return "";
  long storage=capacity<16?a:Ptr(p,a);byte[] bytes=Read(p,storage,(int)size);
  string value=new UTF8Encoding(false,true).GetString(bytes);
  byte[] again=Read(p,a,32);for(int j=0;j<32;j++)Need(header[j]==again[j],"identifier_string_changed");
  return value;
 }
 static Dictionary<string,object> Key(IntPtr p,long b,long item,long itemVt){
  var r=new Dictionary<string,object>();
  if(Ptr(p,itemVt+0x58)!=b+0x1a5b4a0){r["kind"]="unsupported_or_separator";return r;}
  long msg=item+0x120;Need(Ptr(p,msg)==b+0x8d9e9a8,"message_struct_vtable_mismatch");
  uint discriminator=U32(p,msg+0x94),number=U32(p,msg+0xd0);
  string session=SmallString(p,msg+0x70),fallback=SmallString(p,msg+0x98);
  string key=discriminator==0&&fallback.Length>0?fallback:session+"_"+number.ToString(System.Globalization.CultureInfo.InvariantCulture)+"_"+discriminator.ToString(System.Globalization.CultureInfo.InvariantCulture);
  Need(session.Length>0&&key.Length>0,"empty_business_key");
  r["kind"]="message_key_candidate";r["keyTag"]=Tag("key:"+key);r["sessionTag"]=Tag("session:"+session);
  r["keyLength"]=key.Length;r["usesFallback"]=discriminator==0&&fallback.Length>0;r["numberNonzero"]=number!=0;r["discriminatorNonzero"]=discriminator!=0;
  return r;
 }
 static void Need(bool ok,string reason){if(!ok)throw new Failure(reason);}
 static string Hex(long n){return "0x"+unchecked((ulong)n).ToString("x");}
 static void Pointer(long n){Need(n>=0x10000&&n<=MaxPointer&&(n&7)==0,"invalid_or_unaligned_pointer");}
 static byte[] Read(IntPtr p,long a,int n){
  Need(a>=0x10000&&n>0&&a<=MaxPointer-n+1,"invalid_read_address");
  Mbi m;Need(VirtualQueryEx(p,new IntPtr(a),out m,new UIntPtr((uint)Marshal.SizeOf(typeof(Mbi)))).ToUInt64()!=0,"memory_query_failed");
  uint protection=m.Protect&0xff;
  Need(m.State==0x1000&&(m.Protect&0x100)==0&&(protection==2||protection==4||protection==8||
   protection==0x20||protection==0x40||protection==0x80),"memory_not_readable");
  Need((ulong)a>=m.Base&&(ulong)a-m.Base<=m.Size&&(ulong)n<=m.Size-((ulong)a-m.Base),"read_crosses_region");
  byte[] data=new byte[n];UIntPtr read;
  Need(ReadProcessMemory(p,new IntPtr(a),data,new UIntPtr((uint)n),out read)&&read.ToUInt64()==(ulong)n,"read_failed");
  return data;
 }
 static long Ptr(IntPtr p,long a){long v=BitConverter.ToInt64(Read(p,a,8),0);Pointer(v);return v;}
 static uint U32(IntPtr p,long a){return BitConverter.ToUInt32(Read(p,a,4),0);}
 static int I32(IntPtr p,long a){return BitConverter.ToInt32(Read(p,a,4),0);}
 static void Signature(IntPtr p,ModuleInfo m,long rva,byte[] wanted){
  Need(rva>=0&&rva+wanted.Length<=m.Size,"signature_outside_module");
  byte[] actual=Read(p,m.Base.ToInt64()+rva,wanted.Length);
  for(int i=0;i<wanted.Length;i++)Need(actual[i]==wanted[i],"signature_mismatch_"+Hex(rva));
 }
 static ulong Identity(IntPtr p){
  uint code;Need(GetExitCodeProcess(p,out code)&&code==259,"process_not_running");
  FileTime c,e,k,u;Need(GetProcessTimes(p,out c,out e,out k,out u),"process_identity_unavailable");return c.Value;
 }
 static ModuleInfo Module(IntPtr p){
  StringBuilder exe=new StringBuilder(32768);int n=exe.Capacity;
  Need(QueryFullProcessImageName(p,0,exe,ref n)&&String.Equals(exe.ToString(),Exe,StringComparison.OrdinalIgnoreCase),"process_path_mismatch");
  IntPtr[] modules=new IntPtr[2048];uint needed;
  Need(EnumProcessModulesEx(p,modules,(uint)(modules.Length*8),out needed,3),"module_query_failed");
  Need(needed<=modules.Length*8&&needed%8==0,"module_query_truncated");
  IntPtr found=IntPtr.Zero;int matches=0;
  for(int i=0;i<needed/8;i++){StringBuilder path=new StringBuilder(32768);
   uint length=GetModuleFileNameEx(p,modules[i],path,path.Capacity);
   if(length>0&&length<path.Capacity-1&&String.Equals(path.ToString(),Dll,StringComparison.OrdinalIgnoreCase)){found=modules[i];matches++;}}
  Need(matches==1,"exact_loaded_module_not_unique");ModuleInfo info;
  Need(GetModuleInformation(p,found,out info,(uint)Marshal.SizeOf(typeof(ModuleInfo))),"module_info_failed");
  Pointer(info.Base.ToInt64());Need(info.Size==202039296,"module_size_mismatch");return info;
 }
 static void VerifyBuild(IntPtr p,ModuleInfo m){
  WeChatPet.Runtime.BuildIdentity.VerifyFile(Dll);
  Signature(p,m,0x1B778F5,new byte[]{0x48,0x8B,0x05,0x94,0x8F,0xDB,0x09});
  Signature(p,m,0x1B7791E,new byte[]{0x48,0x8D,0x05,0xB3,0xED,0x5C,0x07});
  Signature(p,m,0x1B77A00,new byte[]{0x4C,0x8B,0x49,0x10,0x41,0x83,0x79,0x14,0x00,0x74,0x3D,0x45,0x8B,0x51,0x20});
  Signature(p,m,0x9B13E3,new byte[]{0x48,0x8D,0x05,0x5E,0x8C,0x50,0x08});
  Signature(p,m,0x1B78700,new byte[]{0x48,0x8B,0x41,0x08,0x48,0x8B,0x08,0x48,0x85,0xC9,0x74,0x0C,
   0x8B,0x49,0x04,0x85,0xC9,0x74,0x05,0x48,0x8B,0x40,0x08,0xC3,0x31,0xC0,0xC3});
  Signature(p,m,0x8F62F1,new byte[]{0x48,0x8D,0x15,0xF8,0x50,0x86,0x0A});
  Need(Ptr(p,m.Base.ToInt64()+InterfaceVtable+0x10)==m.Base.ToInt64()+0x1B78700,"object_getter_slot_mismatch");
 }
 static Binding Resolve(IntPtr p,long b,uint id,bool list){
  Binding r=new Binding();r.Cache=Ptr(p,b+RootRva);Need(Ptr(p,r.Cache)==b+CacheVtable,"cache_vtable_mismatch");
  r.Table=Ptr(p,r.Cache+0x10);r.Size=U32(p,r.Table+0x14);r.BucketCount=U32(p,r.Table+0x20);
  Need(r.Size>0&&r.Size<=MaxEntries&&r.BucketCount>0&&r.BucketCount<=MaxEntries,"hash_counts_out_of_bounds");
  r.Seed=U32(p,r.Table+0x24);r.Buckets=Ptr(p,r.Table+0x08);uint hash=id^r.Seed;r.Bucket=hash%r.BucketCount;
  long node=Ptr(p,r.Buckets+8L*r.Bucket);HashSet<long> seen=new HashSet<long>();StringBuilder trace=new StringBuilder();
  while(node!=r.Table){
   Need(r.Visited<MaxChain,"bucket_chain_limit");Need(seen.Add(node),"bucket_chain_cycle");r.Visited++;
   long next=Ptr(p,node);uint nodeHash=U32(p,node+8),nodeId=U32(p,node+12);
   // Internal consistency trace only; no other cache entry IDs are returned or saved.
   trace.Append(node).Append(':').Append(next).Append(':').Append(nodeHash).Append(':').Append(nodeId).Append(';');
   if(nodeHash==hash&&nodeId==id){r.Node=node;r.Interface=Ptr(p,node+0x10);break;}
   node=next;
  }
  Need(r.Interface!=0,"accessible_id_not_found");r.Trace=trace.ToString();
  Need(Ptr(p,r.Interface)==b+(list?InterfaceVtable:0x8EB85C8),"interface_vtable_mismatch");
  r.Private=Ptr(p,r.Interface+8);r.Control=Ptr(p,r.Private);r.RefCount=I32(p,r.Control+4);
  Need(r.RefCount!=0,"object_weak_reference_expired"); // -1 is a possible live QObject weak-ref marker.
  r.Object=Ptr(p,r.Private+8);if(list)Need(Ptr(p,r.Object)==b+ObjectVtable,"recycler_object_vtable_mismatch");
  Need(Ptr(p,b+ObjectVtable)==b+0x8F62C0&&Ptr(p,b+ObjectVtable+8)==b+0x8F62E0,"recycler_meta_slots_mismatch");
  return r;
 }

 public sealed class Row {
  public int AccessibleId; public string KeyTag,SessionTag,SenderTag,AccountId,FriendId,GroupId,SenderId,MemoryKey,SenderEvidence; public uint Type,Created,LocalId; public ulong SortSeq; public bool Supported,Peer,DirectionKnown,GroupChat,GroupScopeKnown,GroupSenderKnown; public int ModelIndex;
 }
 public sealed class Frame {
  public bool Valid,AtEnd; public string Reason; public List<Row> Model=new List<Row>(),Visible=new List<Row>(); public int Count,ScrollMax,ScrollValue; public long ElapsedMs;
 }
 static ulong verifiedCreated; static long verifiedBase; static DateTime verifiedAt;
 internal static string SessionScope(string account,string friend){return account.Length+":"+account+":"+friend;}
 public static void Initialize(byte[] key){Need(key!=null&&key.Length==32,"invalid_tag_key");tagKey=(byte[])key.Clone();}
 static Row Message(IntPtr p,long b,long item,int index){
  long vt=Ptr(p,item);var r=new Row{ModelIndex=index};
  if(Ptr(p,vt+0x58)!=b+0x1a5b4a0)return r;
  var k=Key(p,b,item,vt);long msg=item+0x120;
  r.KeyTag=(string)k["keyTag"];r.SessionTag=(string)k["sessionTag"];r.SenderTag=Tag("sender:"+SmallString(p,msg+0x10));
  string from=SmallString(p,msg+0x10),to=SmallString(p,msg+0x30),session=SmallString(p,msg+0x70);
  r.Peer=from==session;r.GroupChat=IsGroupSession(session);
  r.DirectionKnown=from.Length>0&&to.Length>0&&session.Length>0&&from!=to&&(session==from||session==to)&&(session.IndexOf('@')<0||r.GroupChat);
  string member=r.GroupChat?SmallString(p,msg+0x50):from;
  if(r.GroupChat) {
   string account=r.Peer?to:from;
   r.GroupScopeKnown=ValidGroupScope(from,to,session);
   r.SenderTag=Tag("candidate-member:"+member);
   r.SenderEvidence=member.Length==0?"empty":member==account?"account":member==session?"group":member.IndexOf('@')>=0?"other_scope":"member";
   r.GroupSenderKnown=ValidGroupEndpoints(from,to,session,member);
   r.DirectionKnown=r.DirectionKnown&&r.GroupSenderKnown;
   if(r.DirectionKnown)r.SenderTag=Tag("group-member:"+SessionScope(account,session)+":"+member);
  }
  // A verified group endpoint proves the account and room, not an absent sender.
  // Keep those facts separate so an old senderless row can be omitted safely.
  bool scopeKnown=r.GroupChat?r.GroupScopeKnown:r.DirectionKnown;
  if(scopeKnown)r.SessionTag=Tag("session:"+SessionScope(r.Peer?to:from,session));
  if(scopeKnown&&MemoryIds!=null) {
   string account=r.Peer?to:from;
   r.AccountId=MemoryIds.Account(account);
   if(r.GroupChat){r.GroupId=MemoryIds.Group(account,session);if(r.GroupSenderKnown)r.SenderId=MemoryIds.GroupMember(account,session,member);}
   else r.FriendId=MemoryIds.Friend(account,session);
   string fallback=SmallString(p,msg+0x98);uint discriminator=U32(p,msg+0x94),number=U32(p,msg+0xd0);
   string stable=discriminator==0&&fallback.Length>0?fallback:session+"_"+number.ToString(System.Globalization.CultureInfo.InvariantCulture)+"_"+discriminator.ToString(System.Globalization.CultureInfo.InvariantCulture);
   if(!r.GroupChat)r.MemoryKey=MemoryIds.Message(account,session,stable);
  }
  r.Type=U32(p,msg+8);r.Created=U32(p,msg+0xd0);r.LocalId=U32(p,msg+0x94);r.SortSeq=BitConverter.ToUInt64(Read(p,msg+0xc8,8),0);r.Supported=true;return r;
 }
 internal static bool IsGroupSession(string value){return value!=null&&System.Text.RegularExpressions.Regex.IsMatch(value,@"\A[0-9]{1,32}@chatroom\z");}
 static bool UserIdentifier(string value){return !String.IsNullOrEmpty(value)&&value.Length<=128&&System.Text.RegularExpressions.Regex.IsMatch(value,@"\A[A-Za-z0-9_.-]+\z");}
 internal static bool ValidGroupScope(string from,string to,string session) {
  if(!IsGroupSession(session)||String.IsNullOrEmpty(from)||String.IsNullOrEmpty(to)||from==to||(from!=session&&to!=session))return false;
  return UserIdentifier(from==session?to:from);
 }
 internal static bool ValidGroupEndpoints(string from,string to,string session,string member) {
  if(!ValidGroupScope(from,to,session)||!UserIdentifier(member))return false;
  string account=from==session?to:from;
  return from==session?member!=account:member==account;
 }
 static bool Same(Row a,Row c){return a.GroupChat==c.GroupChat&&a.GroupScopeKnown==c.GroupScopeKnown&&a.GroupSenderKnown==c.GroupSenderKnown&&a.SenderEvidence==c.SenderEvidence&&a.GroupId==c.GroupId&&a.SenderId==c.SenderId&&a.AccountId==c.AccountId&&a.FriendId==c.FriendId&&a.MemoryKey==c.MemoryKey&&a.Supported==c.Supported&&a.DirectionKnown==c.DirectionKnown&&a.Peer==c.Peer&&a.KeyTag==c.KeyTag&&a.SessionTag==c.SessionTag&&a.SenderTag==c.SenderTag&&a.Type==c.Type&&a.Created==c.Created&&a.LocalId==c.LocalId&&a.SortSeq==c.SortSeq;}
 public static Frame Sample(int pid,int listId,int[] visibleIds){
  var f=new Frame();var clock=Stopwatch.StartNew();IntPtr p=IntPtr.Zero;
  try{
   Need(visibleIds!=null&&visibleIds.Length<=100,"row_limit");Need(tagKey!=null,"tag_key_not_initialized");
   p=OpenProcess(0x410,false,pid);Need(p!=IntPtr.Zero,"open_process_failed");
   ulong created=Identity(p);var module=Module(p);long b=module.Base.ToInt64();
   if(created!=verifiedCreated||b!=verifiedBase||(DateTime.UtcNow-verifiedAt).TotalSeconds>30){VerifyBuild(p,module);verifiedCreated=created;verifiedBase=b;verifiedAt=DateTime.UtcNow;}
   var timer=Stopwatch.StartNew();var first=Resolve(p,b,unchecked((uint)listId),true);
   long ds=Ptr(p,first.Object+0x278);Need(Ptr(p,ds)==b+0x9180BA8,"datasource_type");
   long owner=Ptr(p,ds+0x78);Need(Ptr(p,owner)==b+0x958A5A8,"owner_type");
   long begin=Ptr(p,owner+0x88),end=Ptr(p,owner+0x90);Need(end>=begin&&(end-begin)%16==0&&(end-begin)/16<=10000,"vector_bounds");f.Count=(int)((end-begin)/16);
   var members=new Dictionary<long,int>();var controls=new Dictionary<long,long>();var unique=new HashSet<string>();
   for(int ix=Math.Max(0,f.Count-100);ix<f.Count;ix++){
    long item=Ptr(p,begin+ix*16),ctrl=Ptr(p,begin+ix*16+8);Need(I32(p,ctrl+8)>0,"expired_item");
    var r=Message(p,b,item,ix);Need(Same(r,Message(p,b,item,ix)),"message_changed");
    if(r.Supported){Need(unique.Add(r.KeyTag),"duplicate_key");f.Model.Add(r);}
    members.Add(item,ix);controls.Add(item,ctrl);
    Need(item==Ptr(p,begin+ix*16)&&ctrl==Ptr(p,begin+ix*16+8),"entry_changed");
   }
   foreach(int id in visibleIds){
    var row=Resolve(p,b,unchecked((uint)id),false);long item=Ptr(p,row.Object+0x250),ctrl=Ptr(p,row.Object+0x258);
    Need(members.ContainsKey(item)&&controls[item]==ctrl&&I32(p,ctrl+8)>0,"row_model_not_member");
    var r=Message(p,b,item,members[item]);Need(r.Supported,"row_message_unsupported");r.AccessibleId=id;
    Need(Same(r,Message(p,b,item,r.ModelIndex)),"visible_message_changed");
    Need(row.Same(Resolve(p,b,unchecked((uint)id),false))&&item==Ptr(p,row.Object+0x250)&&ctrl==Ptr(p,row.Object+0x258),"row_binding_changed");f.Visible.Add(r);
   }
   long bar=Ptr(p,first.Object+0x1d8);Need(Ptr(p,bar)==b+0x98f1b58,"scroll_type");long priv=Ptr(p,bar+8);byte[] scroll=Read(p,priv+0x1d8,16);
   int min=BitConverter.ToInt32(scroll,0),max=BitConverter.ToInt32(scroll,4),page=BitConverter.ToInt32(scroll,8),value=BitConverter.ToInt32(scroll,12);
   Need(min==0&&max>=min&&page>=0&&value>=min&&value<=max,"scroll_bounds");f.ScrollMax=max;f.ScrollValue=value;f.AtEnd=value==max;
   byte[] again=Read(p,priv+0x1d8,16);for(int j=0;j<16;j++)Need(scroll[j]==again[j],"scroll_changed");
   Need(first.Same(Resolve(p,b,unchecked((uint)listId),true)),"list_changed");
   Need(begin==Ptr(p,owner+0x88)&&end==Ptr(p,owner+0x90)&&owner==Ptr(p,ds+0x78)&&ds==Ptr(p,first.Object+0x278),"model_changed");
   Need(created==Identity(p),"process_changed");Need(timer.ElapsedMilliseconds<=200,"native_sample_stale");
   f.Valid=true;f.Reason="ok";
  }catch(Failure e){f.Reason=e.Message;}catch(Exception e){f.Reason=e.GetType().Name;}finally{if(p!=IntPtr.Zero&&!CloseHandle(p)){f.Valid=false;f.Reason="close_failed";}f.ElapsedMs=clock.ElapsedMilliseconds;}
  return f;
 }
}
