#define wmain vox_tfit2_bridge_embedded_main
#include "../rdr2-tfit2/VOX-RDR2-TFIT2-Bridge.cpp"
#undef wmain

#include <bcrypt.h>
#pragma comment(lib,"bcrypt.lib")

static constexpr const char* YCD_TOOL="VOX RDR2 anim_0 Targeted YCD String Indexer";
static constexpr const char* YCD_VERSION="0.9.1";

struct YEntry {
    uint32_t hash{};
    uint8_t encCfg{},encKey{},extId{},comp{};
    bool resource{},sig{},dir{};
    uint64_t disk{},off{},logical{};
};

static YEntry YParseEntry(const uint8_t* p){
    YEntry e;uint64_t q0=U64LE(p),q8=U64LE(p+8),q10=U64LE(p+16);
    e.hash=(uint32_t)q0;e.encCfg=(uint8_t)(q0>>32);e.encKey=(uint8_t)(q0>>40);e.extId=(uint8_t)(q0>>48);
    e.resource=((q0>>56)&1)!=0;e.sig=((q0>>57)&1)!=0;e.dir=e.extId==0xFE;e.comp=(uint8_t)(q8>>59);
    e.disk=(q8&0x0FFFFFFFull)<<4;e.off=((q8>>28)&0x7FFFFFFFull)<<4;
    e.logical=e.dir?0:(e.resource?((q10&0xFFFFFFF0ull)+((q10>>32)&0xFFFFFFF0ull)):q10);
    return e;
}

static std::vector<std::pair<uint64_t,uint64_t>> YCipherRanges(uint8_t config,uint64_t fileSize,uint64_t chunkSize){
    std::vector<std::pair<uint64_t,uint64_t>> out;uint64_t cursor=0;
    auto add=[&](uint64_t a,uint64_t b){a=std::max(a,cursor);b=std::min(b,fileSize);if(a<b){out.push_back({a,b});cursor=b;}};
    uint64_t tailLen=0x400,tailOff=fileSize>tailLen?fileSize-tailLen:0,head=0,block=0,stride=0;
    if(uint8_t hc=config&3)head=uint64_t(0x400)<<(hc*2);
    if(uint8_t lc=(config>>2)&7){uint8_t sc=(config>>5)&7;block=uint64_t(0x400)<<lc;stride=uint64_t(sc+1)<<16;}
    add(0,head);
    if(head<tailOff){
        if(block||stride){
            if((stride==chunkSize)&&(tailOff<stride)&&(stride<fileSize))cursor=stride;
            else for(uint64_t b=cursor?stride:0;b+block<=tailOff&&stride;b+=stride)add(b,b+block);
        }
        add(tailOff,fileSize);
    }
    return out;
}

static void YDecryptCbcSlice(const Tfit2Context& c,const Tfit2Key& k,uint8_t iv[16],uint8_t* p,size_t n){
    for(size_t b=0;b<n/16;b++){
        uint8_t ct[16],pt[16];std::memcpy(ct,p+b*16,16);DecryptBlock(c,k,ct,pt);
        for(int i=0;i<16;i++)pt[i]^=iv[i];std::memcpy(p+b*16,pt,16);std::memcpy(iv,ct,16);
    }
}

static void YApplyStridedDecrypt(const Tfit2Context& c,const Tfit2Key& k,const uint8_t iv0[16],uint8_t cfg,uint64_t fullSize,uint64_t chunkSize,std::vector<uint8_t>& bytes){
    uint8_t iv[16];std::memcpy(iv,iv0,16);
    for(auto [a,b]:YCipherRanges(cfg,fullSize,chunkSize)){
        if(a>=bytes.size())break;uint64_t end=std::min<uint64_t>(b,bytes.size());if(end<=a)continue;
        size_t n=(size_t)(end-a);n-=n%16;if(n)YDecryptCbcSlice(c,k,iv,bytes.data()+a,n);if(end<b)break;
    }
}

#define Y_OODLE_BLOCK_LEN (1<<18)
#define Y_OODLE_BLOCK_MAX (Y_OODLE_BLOCK_LEN+128)
struct YOodleSomeOut{uint32_t decodedCount,compBufUsed,curQuantumRawLen,curQuantumCompLen;};
struct YOodleApi{
    HMODULE mod{};
    int(__stdcall* MemorySize)(int,intptr_t){};
    void*(__stdcall* Create)(int,long long,void*,size_t){};
    int(__stdcall* Reset)(void*,intptr_t,intptr_t){};
    void(__stdcall* Destroy)(void*){};
    int(__stdcall* DecodeSome)(void*,YOodleSomeOut*,void*,intptr_t,intptr_t,intptr_t,const void*,intptr_t,int,int,int,int){};
};

static bool YLoadOodle(const fs::path& root,YOodleApi& a,std::string& used){
    std::vector<fs::path> cand;
    try{
        for(auto& de:fs::directory_iterator(root)){
            if(!de.is_regular_file())continue;auto n=de.path().filename().string();std::string l=n;
            std::transform(l.begin(),l.end(),l.begin(),[](unsigned char c){return(char)std::tolower(c);});
            if(l.rfind("oo2core_",0)==0&&l.find("_win64.dll")!=std::string::npos)cand.push_back(de.path());
        }
    }catch(...){}
    std::sort(cand.rbegin(),cand.rend());
    for(auto& p:cand){
        HMODULE m=LoadLibraryW(p.c_str());if(!m)continue;a.mod=m;
        a.MemorySize=(decltype(a.MemorySize))GetProcAddress(m,"OodleLZDecoder_MemorySizeNeeded");
        a.Create=(decltype(a.Create))GetProcAddress(m,"OodleLZDecoder_Create");
        a.Reset=(decltype(a.Reset))GetProcAddress(m,"OodleLZDecoder_Reset");
        a.Destroy=(decltype(a.Destroy))GetProcAddress(m,"OodleLZDecoder_Destroy");
        a.DecodeSome=(decltype(a.DecodeSome))GetProcAddress(m,"OodleLZDecoder_DecodeSome");
        if(a.MemorySize&&a.Create&&a.Reset&&a.Destroy&&a.DecodeSome){used=p.filename().string();return true;}
        FreeLibrary(m);a={};
    }
    return false;
}

// Corrected streaming decoder. The important final-input behavior mirrors Swage's
// OodleDecompressor: when all compressed input has been supplied but buffered bytes
// remain, process those remaining bytes instead of immediately reporting exhaustion.
static bool YOodleDecode(YOodleApi& a,const std::vector<uint8_t>& input,uint64_t realSize,size_t want,std::vector<uint8_t>& output,std::string& err){
    want=(size_t)std::min<uint64_t>(want,realSize);output.clear();output.reserve(want);if(!want)return true;
    int mem=a.MemorySize(-1,-1);if(mem<=0){err="oodle_memory_size";return false;}
    std::vector<uint8_t> state((size_t)mem);void* dec=a.Create(-1,(long long)realSize,state.data(),state.size());
    if(!dec){err="oodle_create";return false;}if(a.Reset(dec,0,0)<=0){a.Destroy(dec);err="oodle_reset";return false;}
    std::vector<uint8_t> bin(Y_OODLE_BLOCK_MAX),bout(Y_OODLE_BLOCK_LEN);
    size_t inPos=0,buffered=0,needed=0,outStart=0,outEnd=0,total=0,stalls=0;
    bool ok=true;
    for(size_t guard=0;guard<400000&&total<want;guard++){
        size_t beforeIn=inPos,beforeBuffered=buffered,beforeTotal=total,beforeOutEnd=outEnd;
        if(outEnd>outStart){
            size_t n=std::min(want-total,outEnd-outStart);output.insert(output.end(),bout.begin()+outStart,bout.begin()+outStart+n);
            outStart+=n;total+=n;if(total>=want)break;if(outStart<outEnd)continue;if(outEnd==Y_OODLE_BLOCK_LEN)outStart=outEnd=0;
        }
        if(buffered<needed){
            size_t avail=input.size()-std::min(inPos,input.size());size_t take=std::min(needed-buffered,avail);
            if(take){std::memcpy(bin.data()+buffered,input.data()+inPos,take);buffered+=take;inPos+=take;}
            if(buffered<needed&&inPos>=input.size()){
                if(buffered)needed=buffered;
                else {err="oodle_input_exhausted";ok=false;break;}
            }
        }
        if(buffered==0){
            needed=16;
            if(inPos>=input.size()){err="oodle_no_input";ok=false;break;}
            continue;
        }
        YOodleSomeOut r{};
        int rc=a.DecodeSome(dec,&r,bout.data(),(intptr_t)outEnd,(intptr_t)realSize,(intptr_t)(bout.size()-outEnd),bin.data(),(intptr_t)buffered,0,1,0,3);
        if(rc<=0){err="oodle_decode";ok=false;break;}
        if(r.compBufUsed){
            if(r.compBufUsed>buffered){err="oodle_used_overflow";ok=false;break;}
            size_t extra=buffered-r.compBufUsed;if(extra)std::memmove(bin.data(),bin.data()+r.compBufUsed,extra);buffered=extra;
        }
        needed=buffered;
        if(r.decodedCount){
            outEnd+=r.decodedCount;if(outEnd>bout.size()){err="oodle_output_overflow";ok=false;break;}
        }else{
            if(r.curQuantumRawLen&&outEnd+r.curQuantumRawLen>bout.size()){err="oodle_quantum_raw";ok=false;break;}
            if(r.curQuantumCompLen){if(r.curQuantumCompLen>bin.size()){err="oodle_quantum_comp";ok=false;break;}needed=std::max<size_t>(needed,r.curQuantumCompLen);}
        }
        needed=std::min<size_t>(needed+16,bin.size());
        if(buffered<needed&&inPos<input.size()){
            size_t take=std::min(needed-buffered,input.size()-inPos);std::memcpy(bin.data()+buffered,input.data()+inPos,take);buffered+=take;inPos+=take;
        }
        bool progress=(inPos!=beforeIn)||(buffered!=beforeBuffered)||(total!=beforeTotal)||(outEnd!=beforeOutEnd)||(r.compBufUsed!=0)||(r.decodedCount!=0);
        if(progress)stalls=0;else if(++stalls>8){err="oodle_stalled";ok=false;break;}
    }
    if(total<want&&outEnd>outStart){size_t n=std::min(want-total,outEnd-outStart);output.insert(output.end(),bout.begin()+outStart,bout.begin()+outStart+n);total+=n;}
    a.Destroy(dec);
    if(ok&&output.size()>=want)return true;
    if(err.empty())err="oodle_short_output";
    return false;
}

static std::vector<uint8_t> YReadEntryFile(const fs::path& file,uint64_t fileSize,const YEntry& e,const Tfit2Context& ctx,const std::map<uint16_t,Tfit2Key>& keys,const uint8_t iv[16],YOodleApi* oodle,size_t want,std::string& status){
    if(e.dir||!e.logical){status="empty";return{};}uint64_t raw=e.disk,off=e.off;
    if(e.sig){if(raw<0x100){status="bad_signature_size";return{};}raw-=0x100;}
    if(e.resource){if(raw<16){status="bad_resource_size";return{};}off+=16;raw-=16;}
    uint64_t streamLen=e.comp?raw:e.logical;if(off>=fileSize){status="out_of_bounds";return{};}
    size_t rawCap=(size_t)std::min<uint64_t>(streamLen,fileSize-off);auto b=ReadBytes(file,off,rawCap);
    if(e.encKey!=0xFF){auto it=keys.find(e.encKey);if(it==keys.end()){status="missing_entry_key";return{};}uint64_t chunk=e.resource?(e.comp?0x80000:e.logical):(e.comp?0x2000:0x1000);YApplyStridedDecrypt(ctx,it->second,iv,e.encCfg,streamLen,chunk,b);}
    if(e.comp==0){if(b.size()>want)b.resize(want);status="ok_uncompressed";return b;}
    if(e.comp==2){if(!oodle){status="oodle_unavailable";return{};}std::vector<uint8_t> out;std::string er;if(!YOodleDecode(*oodle,b,e.logical,want,out,er)){status=er;return out;}status="ok_oodle";return out;}
    status="unsupported_compressor_"+std::to_string(e.comp);return{};
}

static std::vector<uint8_t> YReadEntryMemory(const std::vector<uint8_t>& container,const YEntry& e,const Tfit2Context& ctx,const std::map<uint16_t,Tfit2Key>& keys,const uint8_t iv[16],YOodleApi* oodle,size_t want,std::string& status){
    if(e.dir||!e.logical){status="empty";return{};}uint64_t raw=e.disk,off=e.off;
    if(e.sig){if(raw<0x100){status="bad_signature_size";return{};}raw-=0x100;}
    if(e.resource){if(raw<16){status="bad_resource_size";return{};}off+=16;raw-=16;}
    uint64_t streamLen=e.comp?raw:e.logical;if(off>=container.size()){status="out_of_bounds";return{};}
    size_t rawCap=(size_t)std::min<uint64_t>(streamLen,container.size()-off);std::vector<uint8_t> b(container.begin()+off,container.begin()+off+rawCap);
    if(e.encKey!=0xFF){auto it=keys.find(e.encKey);if(it==keys.end()){status="missing_entry_key";return{};}uint64_t chunk=e.resource?(e.comp?0x80000:e.logical):(e.comp?0x2000:0x1000);YApplyStridedDecrypt(ctx,it->second,iv,e.encCfg,streamLen,chunk,b);}
    if(e.comp==0){if(b.size()>want)b.resize(want);status="ok_uncompressed";return b;}
    if(e.comp==2){if(!oodle){status="oodle_unavailable";return{};}std::vector<uint8_t> out;std::string er;if(!YOodleDecode(*oodle,b,e.logical,want,out,er)){status=er;return out;}status="ok_oodle";return out;}
    status="unsupported_compressor_"+std::to_string(e.comp);return{};
}

static bool YParseRpf(const std::vector<uint8_t>& bytes,const Tfit2Context& ctx,const std::map<uint16_t,Tfit2Key>& keys,const uint8_t iv[16],uint16_t& platform,uint16_t& tag,std::vector<YEntry>& entries,std::string& rawSha);

static std::string YSha256(const std::vector<uint8_t>& data){
    BCRYPT_ALG_HANDLE alg=nullptr;BCRYPT_HASH_HANDLE hash=nullptr;DWORD objLen=0,hashLen=0,cb=0;
    if(BCryptOpenAlgorithmProvider(&alg,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0)throw std::runtime_error("BCryptOpenAlgorithmProvider failed");
    BCryptGetProperty(alg,BCRYPT_OBJECT_LENGTH,(PUCHAR)&objLen,sizeof(objLen),&cb,0);BCryptGetProperty(alg,BCRYPT_HASH_LENGTH,(PUCHAR)&hashLen,sizeof(hashLen),&cb,0);
    std::vector<uint8_t> obj(objLen),dig(hashLen);if(BCryptCreateHash(alg,&hash,obj.data(),(ULONG)obj.size(),nullptr,0,0)<0)throw std::runtime_error("BCryptCreateHash failed");
    if(!data.empty())BCryptHashData(hash,(PUCHAR)data.data(),(ULONG)data.size(),0);BCryptFinishHash(hash,dig.data(),(ULONG)dig.size(),0);BCryptDestroyHash(hash);BCryptCloseAlgorithmProvider(alg,0);
    std::ostringstream s;s<<std::hex<<std::setfill('0');for(uint8_t b:dig)s<<std::setw(2)<<(int)b;return s.str();
}

static bool YParseRpf(const std::vector<uint8_t>& bytes,const Tfit2Context& ctx,const std::map<uint16_t,Tfit2Key>& keys,const uint8_t iv[16],uint16_t& platform,uint16_t& tag,std::vector<YEntry>& entries,std::string& rawSha){
    if(bytes.size()<16||std::memcmp(bytes.data(),"8FPR",4)!=0)return false;uint32_t n=U32LE(bytes.data()+4);std::memcpy(&tag,bytes.data()+12,2);std::memcpy(&platform,bytes.data()+14,2);
    uint64_t need=TOC_OFFSET+(uint64_t)n*24;if(n>1000000||need>bytes.size())return false;
    std::vector<uint8_t> raw(bytes.begin()+TOC_OFFSET,bytes.begin()+need);rawSha=YSha256(raw);std::vector<uint8_t> toc=raw;
    if(tag!=0xFF){auto it=keys.find(tag);if(it==keys.end())return false;DecryptCbc(ctx,it->second,iv,toc);}
    entries.reserve(n);for(uint32_t i=0;i<n;i++)entries.push_back(YParseEntry(toc.data()+i*24));return true;
}

static std::string YMagic(const std::vector<uint8_t>& b){if(b.size()<4)return"";bool p=true;for(int i=0;i<4;i++)if(b[i]<32||b[i]>126)p=false;if(p)return std::string((const char*)b.data(),4);std::ostringstream s;s<<"0x"<<std::hex<<std::setfill('0');for(int i=0;i<4;i++)s<<std::setw(2)<<(int)b[i];return s.str();}

static std::vector<std::string> YUsefulStrings(const std::vector<uint8_t>& b){
    std::vector<std::string> out;std::string cur;
    auto flush=[&](){
        if(cur.size()<5){cur.clear();return;}std::string l=cur;std::transform(l.begin(),l.end(),l.begin(),[](unsigned char c){return(char)std::tolower(c);});
        bool useful=cur.find('@')!=std::string::npos||l.find("pack:/")!=std::string::npos||l.find(".clip")!=std::string::npos||l.find("skel_")!=std::string::npos||l.find("ph_")!=std::string::npos||l.find("ik_")!=std::string::npos||l.find("greet")!=std::string::npos||l.find("antagon")!=std::string::npos||l.find("threat")!=std::string::npos||l.find("defus")!=std::string::npos||l.find("melee")!=std::string::npos||l.find("grapple")!=std::string::npos||l.find("combat")!=std::string::npos||l.find("robbery")!=std::string::npos||l.find("intimid")!=std::string::npos||l.find("door")!=std::string::npos||l.find("ragdoll")!=std::string::npos||l.find("getup")!=std::string::npos||l.find("react")!=std::string::npos||l.find("loco")!=std::string::npos||l.find("strafe")!=std::string::npos||l.find("climb")!=std::string::npos||l.find("busted")!=std::string::npos||l.find("revive")!=std::string::npos;
        if(useful&&std::find(out.begin(),out.end(),cur)==out.end())out.push_back(cur.substr(0,220));cur.clear();
    };
    for(uint8_t x:b){if(x>=32&&x<=126){if(cur.size()<400)cur.push_back((char)x);}else flush();}flush();return out;
}

struct YTarget{uint32_t index,hash;const char* name;const char* system;const char* rawSha;};
static const YTarget YTargets[]={
    {5,0x024D61CE,"clip_ai_gestures.rpf","interaction","b55e08fdb3805e38aab45af1abf2cbe93c080d46e2ccf3f1546f99d9454ba2bc"},
    {543,0xEEA37994,"clip_script_common.rpf","interaction","79b579ba0947d39b54bf73157934a47ad4e327f1e8874e5fe652a6f885aa1f3b"},
    {183,0x568C1BB5,"clip_ai_combat.rpf","melee","a067c3ce28dc095c7ae22842fac53e328a3c3c5063fcfdb373663b7cd5d0436a"},
    {410,0xBD0B90DE,"clip_mech_melee.rpf","melee","bcbb1c2d1a0dd1bbf15eee05dd92bbce20eec345d606420559f06f6d95441836"},
    {508,0xE0296150,"clip_mech_grapple.rpf","melee","64491c127657f55759801554ecd70dadbf4b14048e46e7098b47f21f761e87e8"},
    {205,0x60D83661,"clip_mech_loco_m.rpf","locomotion","c03fd34309c11ca2b5483e9e055fdda79ecb5dc0598a6ed9d9b00c9059868fc7"},
    {9,0x05B77FF9,"clip_mech_loco_f.rpf","locomotion","5cf307911c409ef44a34ac599b28dfc49ac01ca2bdd41ec1a8d74b3d97376044"},
    {432,0xC593BAF1,"clip_ai_getup.rpf","locomotion;reactions","0ff2e6babfcbebde1afd000473a17dd71af1cd6dd25ba45119644e7b09d7a66e"},
    {79,0x2917967E,"clip_ai_react.rpf","interaction;reactions","d2832079077d0ce16f06ee66dac7b3358277917b75fe6cdc89c63d3451b96a48"},
    {239,0x7157544D,"clip_ai_ragdoll.rpf","reactions","68cc269097a736958467fd07f1cd7498c071c919a329a3eac25d500ad1a78a70"},
    {16,0x082E9FEB,"clip_mech_doors.rpf","doors","783c844ab4283d858c749f642cef771aeb020bbe715ca36106277da5f0931bbc"},
    {44,0x193DCEA0,"clip_mech_busted.rpf","police","160f473d61308877195ca86c4d29133b6bb0b8bca9e209e0355cfa3d0986aaff"},
    {438,0xC7413AE8,"clip_mech_revive.rpf","reactions","03c773fa1ad5da2399e937a067f2cb9932a6ecd55ccd741090b24fdb5ea94f0a"}
};

static void YBuildSecrets(const fs::path& hashes,const fs::path& rpf8src,std::vector<SecretSpec>& specs,std::map<uint16_t,size_t>& keySpec,size_t& ctxStart,size_t& ivSpec){
    std::string htxt=ReadText(hashes),ctxt=ReadText(rpf8src);auto keyfps=QuotedStrings(ExtractInitializerBody(htxt,"RDR2_PC_KEY_HASHES[166]"));auto ctxfps=QuotedStrings(ExtractInitializerBody(htxt,"RDR2_PC_TFIT2_HASHES"));std::string ivfp=ParseIvFingerprint(ctxt);
    if(keyfps.size()!=166||ctxfps.size()!=565)throw std::runtime_error("fingerprint schema mismatch");
    for(uint16_t i=0;i<163;i++){keySpec[i]=specs.size();AddSpec(specs,"key_"+Hex(i,2),keyfps[i],sizeof(Tfit2Key));}
    for(auto [tag,idx]:std::vector<std::pair<uint16_t,int>>{{0xC0,163},{0xC5,164},{0xC6,165}}){keySpec[tag]=specs.size();AddSpec(specs,"key_"+Hex(tag,2),keyfps[idx],sizeof(Tfit2Key));}
    ctxStart=specs.size();size_t c=0;AddSpec(specs,"ctx_init",ctxfps[c++],sizeof(((Tfit2Context*)0)->init));
    for(int r=0;r<17;r++){AddSpec(specs,"ctx_lookup",ctxfps[c++],sizeof(((Tfit2Context*)0)->rounds[0].lookup));for(int b=0;b<16;b++){AddSpec(specs,"ctx_masks",ctxfps[c++],sizeof(((Tfit2Context*)0)->rounds[0].blocks[0].masks));AddSpec(specs,"ctx_xor",ctxfps[c++],4);}}
    AddSpec(specs,"ctx_end_masks",ctxfps[c++],sizeof(((Tfit2Context*)0)->endMasks));AddSpec(specs,"ctx_end_tables",ctxfps[c++],sizeof(((Tfit2Context*)0)->endTables));AddSpec(specs,"ctx_end_xor",ctxfps[c++],sizeof(((Tfit2Context*)0)->endXor));ivSpec=specs.size();AddSpec(specs,"iv",ivfp,16);
}

static void YMaterialize(const std::vector<SecretSpec>& specs,const std::map<uint16_t,size_t>& keySpec,size_t ctxStart,size_t ivSpec,Tfit2Context& ctx,std::map<uint16_t,Tfit2Key>& keys,uint8_t iv[16]){
    size_t si=ctxStart;CopyExact(ctx.init,sizeof(ctx.init),specs[si++]);for(int r=0;r<17;r++){CopyExact(ctx.rounds[r].lookup,sizeof(ctx.rounds[r].lookup),specs[si++]);for(int b=0;b<16;b++){CopyExact(ctx.rounds[r].blocks[b].masks,sizeof(ctx.rounds[r].blocks[b].masks),specs[si++]);CopyExact(&ctx.rounds[r].blocks[b].xorr,4,specs[si++]);}}
    CopyExact(ctx.endMasks,sizeof(ctx.endMasks),specs[si++]);CopyExact(ctx.endTables,sizeof(ctx.endTables),specs[si++]);CopyExact(ctx.endXor,sizeof(ctx.endXor),specs[si++]);CopyExact(iv,16,specs[ivSpec]);
    for(auto& [tag,idx]:keySpec){Tfit2Key k{};CopyExact(&k,sizeof(k),specs[idx]);keys[tag]=k;}
}

static bool YSelfTest(){
    std::vector<uint8_t> v={'a','b','c'};if(YSha256(v)!="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")return false;
    std::vector<uint8_t> s={'m','e','c','h','_','m','e','l','e','e','@','t','e','s','t'};if(YUsefulStrings(s).empty())return false;
    if(std::size(YTargets)!=13||YTargets[0].index!=5||YTargets[0].hash!=0x024D61CE)return false;
    std::cout<<"[SELFTEST] v0.9.1 target/SHA/string logic OK\n";return true;
}

int wmain(int argc,wchar_t** argv){
    try{
        if(HasArg(argc,argv,L"--self-test"))return (SelfTest()&&YSelfTest())?0:2;
        fs::path root=FindArg(argc,argv,L"--root"),hashes=FindArg(argc,argv,L"--fingerprints"),rpf8src=FindArg(argc,argv,L"--rpf8-source"),out=FindArg(argc,argv,L"--out");
        if(HasArg(argc,argv,L"--fingerprint-self-test")){if(hashes.empty()||rpf8src.empty())throw std::runtime_error("fingerprint inputs required");return FingerprintSelfTest(hashes,rpf8src)?0:2;}
        if(root.empty()||hashes.empty()||rpf8src.empty())throw std::runtime_error("Usage: --root <RDR2> --fingerprints <RDR2.h> --rpf8-source <rpf8.cpp> [--out folder]");
        if(out.empty())out=fs::current_path()/"VOX-RDR2-ANIM0-YCD-Index-v091";fs::create_directories(out);
        if(!fs::is_regular_file(root/"RDR2.exe")||!fs::is_regular_file(root/"anim_0.rpf"))throw std::runtime_error("RDR2.exe or anim_0.rpf missing");

        std::vector<SecretSpec> specs;std::map<uint16_t,size_t> keySpec;size_t ctxStart=0,ivSpec=0;YBuildSecrets(hashes,rpf8src,specs,keySpec,ctxStart,ivSpec);
        DWORD pid=FindProcessId(L"RDR2.exe");if(!pid)throw std::runtime_error("RDR2.exe is not running");HANDLE proc=OpenProcess(PROCESS_QUERY_INFORMATION|PROCESS_VM_READ,FALSE,pid);if(!proc)throw std::runtime_error("Cannot open RDR2 memory read-only; run as administrator");
        std::cout<<"[MEM] Discovering all PC RPF8 entry keys/context...\n";bool discovered=DiscoverSecrets(proc,specs);CloseHandle(proc);if(!discovered)throw std::runtime_error("TFIT2 discovery incomplete: "+std::to_string(FoundCount(specs))+"/"+std::to_string(specs.size()));
        auto ctxp=std::make_unique<Tfit2Context>();Tfit2Context& ctx=*ctxp;std::map<uint16_t,Tfit2Key> keys;uint8_t iv[16];YMaterialize(specs,keySpec,ctxStart,ivSpec,ctx,keys,iv);
        YOodleApi oa;std::string oodleName;if(!YLoadOodle(root,oa,oodleName))throw std::runtime_error("Oodle DLL not found");std::cout<<"[OODLE] loaded "<<oodleName<<"\n";

        ArchiveInfo a=ReadArchiveHeader(root,"anim_0.rpf");auto toc=ReadBytes(a.full,TOC_OFFSET,(size_t)a.entryCount*24);if(a.tag!=0xFF)DecryptCbc(ctx,keys.at(a.tag),iv,toc);char platform=(char)(a.platform&0xFF);
        std::ofstream ta(out/"YCD-target-archives.csv",std::ios::binary),ys(out/"YCD-entry-summary.csv",std::ios::binary),si(out/"YCD-string-index.csv",std::ios::binary);
        ta<<"archive,system,outer_index,outer_hash,expected_raw_toc_sha256,actual_raw_toc_sha256,identity_exact,nested_entries,outer_logical_size\n";
        ys<<"archive,system,outer_index,nested_index,ycd_hash,status,magic,logical_size,on_disk_size,enc_key,enc_config,compressor,decoded_bytes,useful_strings,complete_resource\n";
        si<<"archive,system,outer_index,nested_index,ycd_hash,string\n";
        uint64_t packsExact=0,ycdInspected=0,ycdOk=0,ycdComplete=0,stringCount=0;

        for(size_t ti=0;ti<std::size(YTargets);++ti){
            const auto& t=YTargets[ti];std::cout<<"[PACK "<<(ti+1)<<"/"<<std::size(YTargets)<<"] "<<t.name<<" ...\n";
            if(t.index>=a.entryCount)throw std::runtime_error(std::string("Target index out of range: ")+t.name);YEntry outer=YParseEntry(toc.data()+t.index*24);
            if(outer.hash!=t.hash)throw std::runtime_error(std::string("Target outer hash mismatch: ")+t.name);
            std::string ost;auto pack=YReadEntryFile(a.full,a.size,outer,ctx,keys,iv,&oa,(size_t)outer.logical,ost);if(ost.rfind("ok_",0)!=0||pack.size()!=outer.logical)throw std::runtime_error(std::string("Could not fully read target pack ")+t.name+": "+ost);
            uint16_t np=0,nt=0;std::vector<YEntry> ne;std::string rawSha;if(!YParseRpf(pack,ctx,keys,iv,np,nt,ne,rawSha))throw std::runtime_error(std::string("Nested RPF parse failed: ")+t.name);
            bool exact=rawSha==t.rawSha;if(!exact)throw std::runtime_error(std::string("RAW TOC SHA mismatch: ")+t.name);packsExact++;
            ta<<Csv(t.name)<<","<<Csv(t.system)<<","<<t.index<<","<<Hex(t.hash,8)<<","<<t.rawSha<<","<<rawSha<<",true,"<<ne.size()<<","<<outer.logical<<"\n";
            char nplat=(char)(np&0xFF);size_t packYcd=0,packOk=0;
            for(uint32_t j=0;j<ne.size();++j){auto& e=ne[j];if(ExtFor(e.extId,nplat)!="ycd")continue;packYcd++;ycdInspected++;
                std::string st;auto data=YReadEntryMemory(pack,e,ctx,keys,iv,&oa,(size_t)e.logical,st);auto strings=YUsefulStrings(data);bool ok=st.rfind("ok_",0)==0&&data.size()==e.logical;if(ok){ycdOk++;packOk++;ycdComplete++;}stringCount+=strings.size();
                ys<<Csv(t.name)<<","<<Csv(t.system)<<","<<t.index<<","<<j<<","<<Hex(e.hash,8)<<","<<st<<","<<Csv(YMagic(data))<<","<<e.logical<<","<<e.disk<<","<<(int)e.encKey<<","<<(int)e.encCfg<<","<<(int)e.comp<<","<<data.size()<<","<<strings.size()<<","<<(ok?"true":"false")<<"\n";
                for(auto& s:strings)si<<Csv(t.name)<<","<<Csv(t.system)<<","<<t.index<<","<<j<<","<<Hex(e.hash,8)<<","<<Csv(s)<<"\n";
                if(packYcd%100==0)std::cout<<"  [YCD] "<<packYcd<<" processed; full="<<packOk<<"\n";
            }
            std::cout<<"  [DONE] ycd="<<packYcd<<" full="<<packOk<<"\n";
        }
        ta.close();ys.close();si.close();if(oa.mod)FreeLibrary(oa.mod);
        std::ofstream js(out/"YCD-index-summary.json",std::ios::binary);js<<"{\n  \"tool\":\""<<YCD_TOOL<<"\",\n  \"version\":\""<<YCD_VERSION<<"\",\n  \"target_archives_expected\":"<<std::size(YTargets)<<",\n  \"target_archives_exact\":"<<packsExact<<",\n  \"ycd_inspected\":"<<ycdInspected<<",\n  \"ycd_full_decode_ok\":"<<ycdOk<<",\n  \"useful_strings\":"<<stringCount<<",\n  \"raw_assets_written\":false\n}\n";js.close();
        std::ofstream rp(out/"YCD-index-report.txt",std::ios::binary);rp<<YCD_TOOL<<" v"<<YCD_VERSION<<"\nREAD-ONLY / metadata and strings only\nTarget archives exact: "<<packsExact<<"/"<<std::size(YTargets)<<"\nYCD inspected: "<<ycdInspected<<"\nYCD full decode OK: "<<ycdOk<<"\nUseful strings indexed: "<<stringCount<<"\n";rp.close();
        std::cout<<"[OK] v0.9.1 complete: packs="<<packsExact<<"/"<<std::size(YTargets)<<" ycd="<<ycdInspected<<" full="<<ycdOk<<" strings="<<stringCount<<"\n";return 0;
    }catch(const std::exception& e){std::cerr<<"[ERROR] "<<e.what()<<"\n";return 1;}
}
