#define NOMINMAX
#include <windows.h>
#include <tlhelp32.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <future>
#include <iomanip>
#include <iostream>
#include <map>
#include <mutex>
#include <optional>
#include <regex>
#include <set>
#include <sstream>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

namespace fs = std::filesystem;

static constexpr const char* TOOL = "VOX RDR2 TFIT2 Metadata Bridge";
static constexpr const char* VERSION = "0.4.0";
static constexpr uint32_t POLY_FACTOR = 0x9E3779B1u;
static constexpr uint64_t CRC64_POLY = 0xC96C5795D7870F42ull;
static constexpr size_t ENTRY_SIZE = 24;
static constexpr size_t HEADER_SIZE = 16;
static constexpr size_t RSA_SIZE = 256;
static constexpr size_t TOC_OFFSET = HEADER_SIZE + RSA_SIZE;

struct SecretId {
    uint32_t length{};
    uint32_t hash{};
    uint64_t crc{};
};

struct SecretSpec {
    std::string name;
    std::string fingerprint;
    SecretId id{};
    size_t expected{};
    bool found{false};
    std::vector<uint8_t> data;
};

struct ArchiveInfo {
    fs::path full;
    std::string rel;
    uint32_t entryCount{};
    uint32_t namesLength{};
    uint16_t tag{};
    uint16_t platform{};
    uint64_t size{};
};

#pragma pack(push, 1)
struct Tfit2Key {
    uint64_t data[18][2]{};
};
struct Tfit2Context {
    uint64_t init[16][256]{};
    struct Round {
        uint64_t lookup[4096]{};
        struct Block {
            uint64_t masks[16]{};
            uint32_t xorr{};
        } blocks[16]{};
    } rounds[17]{};
    uint64_t endMasks[16][8]{};
    uint8_t endTables[16][256]{};
    uint8_t endXor[16]{};
};
#pragma pack(pop)

static std::string ReadText(const fs::path& p) {
    std::ifstream f(p, std::ios::binary);
    if (!f) throw std::runtime_error("Cannot open text file: " + p.string());
    return std::string(std::istreambuf_iterator<char>(f), {});
}
static std::vector<uint8_t> ReadBytes(const fs::path& p, uint64_t off, size_t n) {
    std::ifstream f(p, std::ios::binary);
    if (!f) throw std::runtime_error("Cannot open archive: " + p.string());
    f.seekg((std::streamoff)off);
    std::vector<uint8_t> v(n);
    f.read((char*)v.data(), (std::streamsize)n);
    if ((size_t)f.gcount() != n) throw std::runtime_error("Short read: " + p.string());
    return v;
}
static uint32_t U32LE(const uint8_t* p) { uint32_t v; std::memcpy(&v,p,4); return v; }
static uint64_t U64LE(const uint8_t* p) { uint64_t v; std::memcpy(&v,p,8); return v; }
static std::string Hex(uint64_t v, int width) {
    std::ostringstream s; s<<"0x"<<std::uppercase<<std::hex<<std::setw(width)<<std::setfill('0')<<v; return s.str();
}
static std::string Csv(const std::string& s) {
    if (s.find_first_of(",\"\r\n")==std::string::npos) return s;
    std::string o="\""; for(char c:s){ if(c=='\"')o+="\"\""; else o+=c; } o+="\""; return o;
}
static std::string Json(const std::string& s) {
    std::string o; for(unsigned char c:s){ switch(c){case '\\':o+="\\\\";break;case '"':o+="\\\"";break;case '\n':o+="\\n";break;case '\r':o+="\\r";break;case '\t':o+="\\t";break;default: if(c<32){char b[7];sprintf_s(b,"\\u%04x",c);o+=b;}else o+=(char)c;} } return o;
}

static std::array<uint64_t,256> MakeCrcTable() {
    std::array<uint64_t,256> t{};
    for (uint64_t i=0;i<256;i++) {
        uint64_t c=i;
        for(int j=0;j<8;j++) c=(c&1)?((c>>1)^CRC64_POLY):(c>>1);
        t[(size_t)i]=c;
    }
    return t;
}
static const auto CRC_TABLE = MakeCrcTable();
static uint64_t Crc64(const uint8_t* p, size_t n) {
    uint64_t c=~0ull;
    for(size_t i=0;i<n;i++) c=(c>>8)^CRC_TABLE[(uint8_t)(c^p[i])];
    return ~c;
}
static uint32_t PolyHash(const uint8_t* p, size_t n) {
    uint32_t h=0; for(size_t i=0;i<n;i++) h=(uint32_t)((h+p[i])*POLY_FACTOR); return h;
}
static uint32_t Pow32(uint32_t x, size_t n) {
    uint32_t r=1; while(n){if(n&1)r=(uint32_t)((uint64_t)r*x);x=(uint32_t)((uint64_t)x*x);n>>=1;} return r;
}

static std::vector<uint8_t> Decode85(const std::string& s) {
    static const std::string alpha="0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz!#$%&()*+-;<=>?@^_`{|}~";
    std::array<int,128> dec{}; dec.fill(-1);
    for(size_t i=0;i<alpha.size();++i) dec[(unsigned char)alpha[i]]=(int)i;
    std::vector<uint8_t> out;
    size_t pos=0;
    while(pos<s.size()){
        uint32_t acc=0; size_t valid=0;
        for(int j=0;j<5;j++){
            uint32_t v=84;
            if(pos<s.size()){
                unsigned char c=(unsigned char)s[pos++];
                if(c>=128 || dec[c]<0) throw std::runtime_error("Invalid base85 fingerprint");
                v=(uint32_t)dec[c]; valid++;
            }
            acc=(uint32_t)((uint64_t)acc*85u+v);
        }
        for(size_t j=1;j<valid;j++){ out.push_back((uint8_t)(acc>>24)); acc<<=8; }
    }
    return out;
}
static SecretId ParseSecretId(const std::string& fp) {
    auto b=Decode85(fp);
    if(b.size()!=16) throw std::runtime_error("Secret fingerprint did not decode to 16 bytes");
    SecretId id; id.length=U32LE(b.data()); id.hash=U32LE(b.data()+4); id.crc=U64LE(b.data()+8); return id;
}
static std::vector<std::string> QuotedStrings(const std::string& body) {
    std::vector<std::string> out;
    std::regex re("\"([^\"]+)\"");
    for(std::sregex_iterator i(body.begin(),body.end(),re),e;i!=e;++i) out.push_back((*i)[1].str());
    return out;
}
static std::string ExtractInitializerBody(const std::string& text, const std::string& marker) {
    size_t p=text.find(marker); if(p==std::string::npos) throw std::runtime_error("Fingerprint marker not found: "+marker);
    p=text.find('{',p); if(p==std::string::npos) throw std::runtime_error("Initializer start not found");
    size_t e=text.find("\n};",p); if(e==std::string::npos) e=text.find("\r\n};",p);
    if(e==std::string::npos) throw std::runtime_error("Initializer end not found");
    return text.substr(p+1,e-p-1);
}
static std::string ParseIvFingerprint(const std::string& cpp) {
    std::regex re("RAGE_IV_HASH\\s*=\\s*\"([^\"]+)\"");
    std::smatch m; if(!std::regex_search(cpp,m,re)) throw std::runtime_error("RAGE_IV_HASH fingerprint not found");
    return m[1].str();
}

static void AddSpec(std::vector<SecretSpec>& specs, const std::string& name, const std::string& fp, size_t expected) {
    SecretSpec s; s.name=name; s.fingerprint=fp; s.id=ParseSecretId(fp); s.expected=expected;
    if(s.id.length!=expected) {
        std::ostringstream o; o<<"Fingerprint size mismatch for "<<name<<": "<<s.id.length<<" != "<<expected;
        throw std::runtime_error(o.str());
    }
    specs.push_back(std::move(s));
}

static bool RecoverFourByteSecret(SecretSpec& s) {
    if(s.id.length!=4) return false;
    uint32_t f1=POLY_FACTOR, f2=(uint32_t)((uint64_t)f1*f1), f3=(uint32_t)((uint64_t)f2*f1), f4=(uint32_t)((uint64_t)f3*f1);
    std::unordered_multimap<uint32_t,uint16_t> tail;
    tail.reserve(70000);
    for(uint32_t b2=0;b2<256;b2++) for(uint32_t b3=0;b3<256;b3++) {
        uint32_t v=(uint32_t)((uint64_t)b2*f2+(uint64_t)b3*f1);
        tail.emplace(v,(uint16_t)((b2<<8)|b3));
    }
    uint8_t candidate[4];
    for(uint32_t b0=0;b0<256;b0++) for(uint32_t b1=0;b1<256;b1++) {
        uint32_t first=(uint32_t)((uint64_t)b0*f4+(uint64_t)b1*f3);
        uint32_t need=s.id.hash-first;
        auto range=tail.equal_range(need);
        for(auto it=range.first;it!=range.second;++it){
            candidate[0]=(uint8_t)b0; candidate[1]=(uint8_t)b1;
            candidate[2]=(uint8_t)(it->second>>8); candidate[3]=(uint8_t)it->second;
            if(Crc64(candidate,4)==s.id.crc){
                s.data.assign(candidate,candidate+4); s.found=true; return true;
            }
        }
    }
    return false;
}

static DWORD FindProcessId(const std::wstring& exe) {
    HANDLE snap=CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS,0);
    if(snap==INVALID_HANDLE_VALUE) return 0;
    PROCESSENTRY32W pe{}; pe.dwSize=sizeof(pe);
    DWORD pid=0;
    if(Process32FirstW(snap,&pe)){
        do { if(_wcsicmp(pe.szExeFile,exe.c_str())==0){pid=pe.th32ProcessID;break;} } while(Process32NextW(snap,&pe));
    }
    CloseHandle(snap); return pid;
}
static bool ReadableProtect(DWORD p) {
    if(p&PAGE_GUARD || p&PAGE_NOACCESS) return false;
    DWORD b=p&0xFF;
    return b==PAGE_READONLY||b==PAGE_READWRITE||b==PAGE_WRITECOPY||b==PAGE_EXECUTE_READ||b==PAGE_EXECUTE_READWRITE||b==PAGE_EXECUTE_WRITECOPY;
}

static void ScanGroup(const std::vector<uint8_t>& buf, size_t len, const std::vector<size_t>& idxs, std::vector<SecretSpec>& specs) {
    if(len==0 || buf.size()<len) return;
    std::unordered_map<uint32_t,std::vector<size_t>> targets;
    for(size_t idx:idxs) if(!specs[idx].found) targets[specs[idx].id.hash].push_back(idx);
    if(targets.empty()) return;
    uint32_t h=PolyHash(buf.data(),len);
    uint32_t pow=Pow32(POLY_FACTOR,len);
    const size_t last=buf.size()-len;
    for(size_t pos=0;;++pos){
        auto hit=targets.find(h);
        if(hit!=targets.end()){
            for(size_t idx:hit->second){
                auto& s=specs[idx];
                if(!s.found && Crc64(buf.data()+pos,len)==s.id.crc){
                    s.data.assign(buf.begin()+pos,buf.begin()+pos+len); s.found=true;
                }
            }
        }
        if(pos==last) break;
        h=(uint32_t)((uint64_t)(h-(uint32_t)((uint64_t)buf[pos]*pow)+buf[pos+len])*POLY_FACTOR);
    }
}

static bool AllFound(const std::vector<SecretSpec>& specs) {
    return std::all_of(specs.begin(),specs.end(),[](const SecretSpec& s){return s.found;});
}
static size_t FoundCount(const std::vector<SecretSpec>& specs) {
    return (size_t)std::count_if(specs.begin(),specs.end(),[](const SecretSpec& s){return s.found;});
}

static bool DiscoverSecrets(HANDLE proc, std::vector<SecretSpec>& specs) {
    for(auto& s:specs) if(s.id.length==4 && !s.found) RecoverFourByteSecret(s);
    std::map<size_t,std::vector<size_t>> groups;
    size_t maxLen=1;
    for(size_t i=0;i<specs.size();i++) if(!specs[i].found){ groups[specs[i].id.length].push_back(i); maxLen=std::max(maxLen,(size_t)specs[i].id.length); }
    if(groups.empty()) return true;

    SYSTEM_INFO si{}; GetSystemInfo(&si);
    uintptr_t addr=(uintptr_t)si.lpMinimumApplicationAddress;
    uintptr_t maxAddr=(uintptr_t)si.lpMaximumApplicationAddress;
    MEMORY_BASIC_INFORMATION mbi{};
    const size_t CHUNK=32ull*1024*1024;
    uint64_t scanned=0, lastReport=0;

    while(addr<maxAddr && !AllFound(specs)) {
        SIZE_T q=VirtualQueryEx(proc,(LPCVOID)addr,&mbi,sizeof(mbi));
        if(!q) { addr+=0x1000; continue; }
        uintptr_t base=(uintptr_t)mbi.BaseAddress;
        size_t region=(size_t)mbi.RegionSize;
        uintptr_t next=base+region;
        if(next<=addr) break;

        if(mbi.State==MEM_COMMIT && ReadableProtect(mbi.Protect)) {
            size_t pos=0;
            while(pos<region && !AllFound(specs)) {
                size_t overlap=(pos==0)?0:std::min(maxLen-1,pos);
                size_t readStart=pos-overlap;
                size_t wanted=std::min(CHUNK+overlap,region-readStart);
                std::vector<uint8_t> buf(wanted);
                SIZE_T got=0;
                if(ReadProcessMemory(proc,(LPCVOID)(base+readStart),buf.data(),wanted,&got) && got>0) {
                    buf.resize((size_t)got);
                    std::vector<std::future<void>> jobs;
                    for(auto& [len,idxs]:groups) {
                        bool pending=false; for(auto i:idxs) if(!specs[i].found){pending=true;break;}
                        if(pending && len<=buf.size())
                            jobs.push_back(std::async(std::launch::async,[&,len,idxs](){ScanGroup(buf,len,idxs,specs);}));
                    }
                    for(auto& j:jobs) j.get();
                    scanned+=got;
                    if(scanned-lastReport>=256ull*1024*1024) {
                        lastReport=scanned;
                        std::cout<<"[MEM] "<<(scanned/(1024*1024))<<" MiB read; "<<FoundCount(specs)<<"/"<<specs.size()<<" secrets resolved\n";
                    }
                }
                pos+=std::min(CHUNK,region-pos);
            }
        }
        addr=next;
    }
    std::cout<<"[MEM] Discovery finished: "<<FoundCount(specs)<<"/"<<specs.size()<<" required secrets resolved.\n";
    return AllFound(specs);
}

static uint64_t RoundBlock(const uint64_t in[8], const uint64_t masks[16], uint32_t x, const uint64_t lookup[4096]) {
    uint64_t lo=(masks[15]&in[7])^(masks[14]&in[6])^(masks[13]&in[5])^(masks[12]&in[4])^(masks[11]&in[3])^(masks[10]&in[2])^(masks[9]&in[1])^(masks[8]&in[0]);
    lo^=lo>>1; lo^=lo>>2; lo^=lo>>4; lo&=0x0101010101010101ull; lo|=lo>>7; lo|=lo>>14; lo|=lo>>28;
    uint64_t hi=(masks[7]&in[7])^(masks[6]&in[6])^(masks[5]&in[5])^(masks[4]&in[4])^(masks[3]&in[3])^(masks[2]&in[2])^(masks[1]&in[1])^(masks[0]&in[0]);
    hi^=hi<<1; hi^=hi>>2; hi^=hi>>4; hi&=0x0202020202020202ull; hi|=hi<<7; hi|=hi>>14;
    return lookup[(uint32_t)((lo&0xFF)^(hi&0xF00)^x)];
}
static void Splat(const uint8_t* b, uint64_t out[8]) {
    for(int i=0;i<8;i++) out[i]=0x0101010101010101ull*b[7-i];
}
static void RoundA(const Tfit2Context& c, uint8_t d[16]) {
    uint64_t v0=0,v1=0;
    for(int i=0;i<8;i++) v0^=c.init[i][d[i]];
    for(int i=8;i<16;i++) v1^=c.init[i][d[i]];
    std::memcpy(d,&v0,8); std::memcpy(d+8,&v1,8);
}
static void RoundB(const Tfit2Context& c,int r,uint8_t d[16],const uint64_t key[2],bool cross) {
    uint64_t v0[8],v1[8]; Splat(d,v0); Splat(d+8,v1);
    uint64_t a=key[0],b=key[1]; const auto& rr=c.rounds[r];
    if(!cross){
        for(int i=0;i<8;i++) a^=RoundBlock(v0,rr.blocks[i].masks,rr.blocks[i].xorr,rr.lookup);
        for(int i=8;i<16;i++) b^=RoundBlock(v1,rr.blocks[i].masks,rr.blocks[i].xorr,rr.lookup);
    } else {
        const uint64_t* s0[8]={v0,v1,v1,v0,v0,v0,v1,v1};
        const uint64_t* s1[8]={v1,v0,v0,v1,v1,v1,v0,v0};
        for(int i=0;i<8;i++) a^=RoundBlock(s0[i],rr.blocks[i].masks,rr.blocks[i].xorr,rr.lookup);
        for(int i=0;i<8;i++) b^=RoundBlock(s1[i],rr.blocks[i+8].masks,rr.blocks[i+8].xorr,rr.lookup);
    }
    std::memcpy(d,&a,8); std::memcpy(d+8,&b,8);
}
static uint8_t Squash(const uint64_t in[8],const uint64_t masks[8]) {
    uint64_t v=(masks[7]&in[7])^(masks[6]&in[6])^(masks[5]&in[5])^(masks[4]&in[4])^(masks[3]&in[3])^(masks[2]&in[2])^(masks[1]&in[1])^(masks[0]&in[0]);
    v^=v>>1;v^=v>>2;v^=v>>4;v&=0x0101010101010101ull;v|=v>>7;v|=v>>14;v|=v>>28; return (uint8_t)v;
}
static void RoundD(const Tfit2Context& c,uint8_t d[16]) {
    uint64_t v0[8],v1[8]; Splat(d,v0);Splat(d+8,v1);
    for(int i=0;i<8;i++) d[i]=c.endTables[i][Squash(v0,c.endMasks[i])^c.endXor[i]];
    for(int i=8;i<16;i++) d[i]=c.endTables[i][Squash(v1,c.endMasks[i])^c.endXor[i]];
}
static void DecryptBlock(const Tfit2Context& c,const Tfit2Key& k,const uint8_t in[16],uint8_t out[16]) {
    uint8_t d[16]; std::memcpy(d,in,16);
    RoundA(c,d); RoundB(c,0,d,k.data[1],false); RoundB(c,1,d,k.data[2],false);
    for(int r=2;r<16;r++) RoundB(c,r,d,k.data[r+1],true);
    RoundB(c,16,d,k.data[17],false); RoundD(c,d); std::memcpy(out,d,16);
}
static void DecryptCbc(const Tfit2Context& c,const Tfit2Key& k,const uint8_t iv0[16],std::vector<uint8_t>& bytes) {
    uint8_t iv[16];std::memcpy(iv,iv0,16);
    size_t blocks=bytes.size()/16;
    for(size_t n=0;n<blocks;n++){
        uint8_t ct[16],pt[16];std::memcpy(ct,bytes.data()+n*16,16);DecryptBlock(c,k,ct,pt);
        for(int i=0;i<16;i++) pt[i]^=iv[i];
        std::memcpy(bytes.data()+n*16,pt,16);std::memcpy(iv,ct,16);
    }
}

static uint64_t Xs64(uint64_t& x){x^=x<<13;x^=x>>7;x^=x<<17;return x;}
static bool SelfTest() {
    Tfit2Context c{}; Tfit2Key k{}; uint64_t s=0x123456789ABCDEF0ull;
    for(auto& row:c.init)for(auto& v:row)v=Xs64(s);
    for(auto& r:c.rounds){for(auto& v:r.lookup)v=Xs64(s);for(auto& b:r.blocks){for(auto& m:b.masks)m=Xs64(s);b.xorr=(uint32_t)(Xs64(s)&0xFFF);}}
    for(auto& row:c.endMasks)for(auto& v:row)v=Xs64(s);
    for(auto& row:c.endTables)for(auto& v:row)v=(uint8_t)Xs64(s);
    for(auto& v:c.endXor)v=(uint8_t)Xs64(s);
    for(auto& row:k.data){row[0]=Xs64(s);row[1]=Xs64(s);}
    uint8_t iv[16],ct[16];for(auto& v:iv)v=(uint8_t)Xs64(s);for(auto& v:ct)v=(uint8_t)Xs64(s);
    std::vector<uint8_t> b(ct,ct+16);DecryptCbc(c,k,iv,b);
    const uint8_t expected[16]={0x59,0x0f,0x45,0x29,0xd3,0xea,0xeb,0x2d,0xeb,0x90,0x5e,0x7f,0x82,0xc9,0xca,0xf0};
    bool ok=std::equal(b.begin(),b.end(),expected);
    std::cout<<(ok?"[SELFTEST] TFIT2 deterministic vector OK\n":"[SELFTEST] TFIT2 deterministic vector FAILED\n");
    return ok;
}

static std::string ExtFor(uint8_t id,char p) {
    static const char* base[14]={"rpf","#mf","#dr","#ft","#dd","#td","#bn","#bd","#pd","#bs","#sd","#mt","#sc","#cs"};
    static const char* extra[24]={"mrf","cut","gfx","#cd","#ld","#pmd","#pm","#ed","#pt","#map","#typ","#ch","#ldb","#jd","#ad","#nv","#hn","#pl","#nd","#vr","#wr","#nh","#fd","#as"};
    std::string s;
    if(id<=13)s=base[id]; else if(id>=64&&id<=87)s=extra[id-64]; else if(id==0xFE)s="dir"; else s="bin";
    std::replace(s.begin(),s.end(),'#',p); return s;
}
static std::string RawMagic(const fs::path& p,uint64_t off,uint64_t size) {
    if(off+4>size)return "out_of_bounds";
    try{auto b=ReadBytes(p,off,4);std::string a((char*)b.data(),4);bool pr=true;for(unsigned char c:a)if(c<32||c>126)pr=false;
        std::ostringstream o;o<<std::hex<<std::setfill('0');for(auto x:b)o<<std::setw(2)<<(int)x;
        return pr?a:("0x"+o.str());
    }catch(...){return "io_error";}
}
static ArchiveInfo ReadArchiveHeader(const fs::path& root,const std::string& rel) {
    ArchiveInfo a; a.rel=rel;a.full=root/fs::path(rel);if(!fs::is_regular_file(a.full))throw std::runtime_error("Missing archive: "+rel);
    a.size=fs::file_size(a.full);auto h=ReadBytes(a.full,0,16);
    if(std::memcmp(h.data(),"8FPR",4)!=0)throw std::runtime_error("Not physical RPF8/8FPR: "+rel);
    a.entryCount=U32LE(h.data()+4);a.namesLength=U32LE(h.data()+8);std::memcpy(&a.tag,h.data()+12,2);std::memcpy(&a.platform,h.data()+14,2);
    if(TOC_OFFSET+(uint64_t)a.entryCount*ENTRY_SIZE>a.size)throw std::runtime_error("Declared TOC out of bounds: "+rel);
    return a;
}

static void CopyExact(void* dst,size_t n,const SecretSpec& s) {
    if(!s.found||s.data.size()!=n)throw std::runtime_error("Secret unavailable: "+s.name);
    std::memcpy(dst,s.data.data(),n);
}

static std::vector<std::string> DefaultTargets(){
    return {"common_0.rpf","x64/dlcpacks/dlc_content_extra/dlc.rpf","x64/dlcpacks/mp004/dlc.rpf","x64/dlcpacks/mp005/dlc.rpf","x64/dlcpacks/mp006/dlc.rpf","x64/dlcpacks/mp008/dlc.rpf","x64/dlcpacks/patchpack001/dlc.rpf","x64/audio/sfx/S_MISC.rpf"};
}


static bool FingerprintSelfTest(const fs::path& hashes,const fs::path& rpf8src) {
    std::string htxt=ReadText(hashes), ctxt=ReadText(rpf8src);
    auto keyfps=QuotedStrings(ExtractInitializerBody(htxt,"RDR2_PC_KEY_HASHES[166]"));
    auto ctxfps=QuotedStrings(ExtractInitializerBody(htxt,"RDR2_PC_TFIT2_HASHES"));
    std::string ivfp=ParseIvFingerprint(ctxt);
    bool ok=keyfps.size()==166 && ctxfps.size()==565;
    if(ok) {
        ok = ParseSecretId(keyfps[2]).length==sizeof(Tfit2Key)
          && ParseSecretId(ctxfps[0]).length==sizeof(((Tfit2Context*)0)->init)
          && ParseSecretId(ctxfps[1]).length==sizeof(((Tfit2Context*)0)->rounds[0].lookup)
          && ParseSecretId(ctxfps[2]).length==sizeof(((Tfit2Context*)0)->rounds[0].blocks[0].masks)
          && ParseSecretId(ctxfps[3]).length==sizeof(uint32_t)
          && ParseSecretId(ctxfps[562]).length==sizeof(((Tfit2Context*)0)->endMasks)
          && ParseSecretId(ctxfps[563]).length==sizeof(((Tfit2Context*)0)->endTables)
          && ParseSecretId(ctxfps[564]).length==sizeof(((Tfit2Context*)0)->endXor)
          && ParseSecretId(ivfp).length==16;
    }
    std::cout<<(ok?"[SELFTEST] Public fingerprint schema OK\n":"[SELFTEST] Public fingerprint schema FAILED\n");
    std::cout<<"[SELFTEST] keys="<<keyfps.size()<<" context="<<ctxfps.size()<<" iv="<<(ivfp.empty()?0:1)<<"\n";
    return ok;
}

static fs::path FindArg(int argc,wchar_t** argv,const std::wstring& name) {
    for(int i=1;i+1<argc;i++)if(argv[i]==name)return fs::path(argv[i+1]); return {};
}
static bool HasArg(int argc,wchar_t** argv,const std::wstring& name){for(int i=1;i<argc;i++)if(argv[i]==name)return true;return false;}

int wmain(int argc,wchar_t** argv) {
    try {
        if(HasArg(argc,argv,L"--self-test")) return SelfTest()?0:2;
        fs::path root=FindArg(argc,argv,L"--root"), hashes=FindArg(argc,argv,L"--fingerprints"), rpf8src=FindArg(argc,argv,L"--rpf8-source");
        if(HasArg(argc,argv,L"--fingerprint-self-test")) {
            if(hashes.empty()||rpf8src.empty()) throw std::runtime_error("--fingerprint-self-test requires --fingerprints and --rpf8-source");
            return FingerprintSelfTest(hashes,rpf8src)?0:2;
        }
        fs::path out=FindArg(argc,argv,L"--out"); if(out.empty())out=fs::current_path()/"VOX-RDR2-TFIT2-Catalog";
        if(root.empty()||hashes.empty()||rpf8src.empty()){
            std::cerr<<"Usage: VOX-RDR2-TFIT2-Bridge.exe --root <RDR2 dir> --fingerprints <RDR2.h> --rpf8-source <rpf8.cpp> [--out dir]\n";
            return 1;
        }
        if(!fs::is_regular_file(root/"RDR2.exe"))throw std::runtime_error("RDR2.exe not found in selected root.");
        fs::create_directories(out);

        std::vector<ArchiveInfo> archives; std::set<uint16_t> tags;
        for(auto& rel:DefaultTargets()){auto a=ReadArchiveHeader(root,rel);if(a.tag!=0xFF)tags.insert(a.tag);archives.push_back(a);}
        std::cout<<"[RPF8] "<<archives.size()<<" target archives; "<<tags.size()<<" distinct TFIT2 tags required.\n";

        std::string htxt=ReadText(hashes), ctxt=ReadText(rpf8src);
        auto keyfps=QuotedStrings(ExtractInitializerBody(htxt,"RDR2_PC_KEY_HASHES[166]"));
        auto ctxfps=QuotedStrings(ExtractInitializerBody(htxt,"RDR2_PC_TFIT2_HASHES"));
        std::string ivfp=ParseIvFingerprint(ctxt);
        if(keyfps.size()!=166)throw std::runtime_error("Expected 166 PC key fingerprints, got "+std::to_string(keyfps.size()));
        if(ctxfps.size()!=565)throw std::runtime_error("Expected 565 TFIT2 context fingerprints, got "+std::to_string(ctxfps.size()));

        std::vector<SecretSpec> specs; std::map<uint16_t,size_t> keySpec;
        for(uint16_t tag:tags){
            if(tag>=163)throw std::runtime_error("Target uses unsupported special key tag "+Hex(tag,4));
            keySpec[tag]=specs.size(); AddSpec(specs,"key_"+Hex(tag,2),keyfps[tag],sizeof(Tfit2Key));
        }
        size_t ctxStart=specs.size(), c=0;
        AddSpec(specs,"ctx_init_tables",ctxfps[c++],sizeof(((Tfit2Context*)0)->init));
        for(int r=0;r<17;r++){
            AddSpec(specs,"ctx_round_"+std::to_string(r)+"_lookup",ctxfps[c++],sizeof(((Tfit2Context*)0)->rounds[0].lookup));
            for(int b=0;b<16;b++){
                AddSpec(specs,"ctx_round_"+std::to_string(r)+"_block_"+std::to_string(b)+"_masks",ctxfps[c++],sizeof(((Tfit2Context*)0)->rounds[0].blocks[0].masks));
                AddSpec(specs,"ctx_round_"+std::to_string(r)+"_block_"+std::to_string(b)+"_xor",ctxfps[c++],sizeof(uint32_t));
            }
        }
        AddSpec(specs,"ctx_end_masks",ctxfps[c++],sizeof(((Tfit2Context*)0)->endMasks));
        AddSpec(specs,"ctx_end_tables",ctxfps[c++],sizeof(((Tfit2Context*)0)->endTables));
        AddSpec(specs,"ctx_end_xor",ctxfps[c++],sizeof(((Tfit2Context*)0)->endXor));
        size_t ivSpec=specs.size();AddSpec(specs,"iv",ivfp,16);
        if(c!=ctxfps.size())throw std::runtime_error("TFIT2 fingerprint mapping count mismatch.");

        DWORD pid=FindProcessId(L"RDR2.exe");
        if(!pid)throw std::runtime_error("RDR2.exe is not running. Launch RDR2 to the main menu or Story Mode, keep it open, then run this bridge.");
        HANDLE proc=OpenProcess(PROCESS_QUERY_INFORMATION|PROCESS_VM_READ,FALSE,pid);
        if(!proc){DWORD e=GetLastError();throw std::runtime_error("Cannot read RDR2.exe process memory (Win32 "+std::to_string(e)+"). Try running the bridge as administrator.");}
        std::cout<<"[PROC] RDR2.exe PID "<<pid<<"; read-only process access granted.\n";
        bool discovered=DiscoverSecrets(proc,specs);CloseHandle(proc);

        std::ofstream disc(out/"TFIT2-discovery.csv",std::ios::binary);
        disc<<"name,expected_bytes,found\n";
        for(auto& s:specs)disc<<Csv(s.name)<<","<<s.expected<<","<<(s.found?"true":"false")<<"\n";
        disc.close();

        std::ofstream summary(out/"TFIT2-summary.json",std::ios::binary);
        summary<<"{\n  \"tool\":\""<<TOOL<<"\",\n  \"version\":\""<<VERSION<<"\",\n  \"read_only\":true,\n";
        summary<<"  \"process_pid\":"<<pid<<",\n  \"required_secrets\":"<<specs.size()<<",\n  \"found_secrets\":"<<FoundCount(specs)<<",\n";
        summary<<"  \"raw_secrets_written\":false,\n  \"archives_requested\":"<<archives.size()<<",\n  \"fingerprints_source\":\"Swage pinned public source fetched by launcher\",\n";
        summary<<"  \"status\":\""<<(discovered?"discovered":"incomplete_discovery")<<"\"\n}\n"; summary.close();

        if(!discovered){
            std::ofstream rep(out/"TFIT2-report.txt",std::ios::binary);
            rep<<TOOL<<" v"<<VERSION<<"\n\nMemory discovery incomplete: "<<FoundCount(specs)<<"/"<<specs.size()<<" required blocks found.\n";
            rep<<"No key bytes or memory dumps were written. See TFIT2-discovery.csv for missing logical fields only.\n";
            std::cerr<<"[STOP] Required TFIT2 data was not fully found; no TOC was decrypted.\n";
            return 3;
        }

        Tfit2Context ctx{}; size_t si=ctxStart;
        CopyExact(ctx.init,sizeof(ctx.init),specs[si++]);
        for(int r=0;r<17;r++){
            CopyExact(ctx.rounds[r].lookup,sizeof(ctx.rounds[r].lookup),specs[si++]);
            for(int b=0;b<16;b++){CopyExact(ctx.rounds[r].blocks[b].masks,sizeof(ctx.rounds[r].blocks[b].masks),specs[si++]);CopyExact(&ctx.rounds[r].blocks[b].xorr,sizeof(uint32_t),specs[si++]);}
        }
        CopyExact(ctx.endMasks,sizeof(ctx.endMasks),specs[si++]);CopyExact(ctx.endTables,sizeof(ctx.endTables),specs[si++]);CopyExact(ctx.endXor,sizeof(ctx.endXor),specs[si++]);
        uint8_t iv[16];CopyExact(iv,16,specs[ivSpec]);
        std::map<uint16_t,Tfit2Key> keys;for(auto& [tag,idx]:keySpec){Tfit2Key k{};CopyExact(&k,sizeof(k),specs[idx]);keys[tag]=k;}

        std::ofstream acsv(out/"RPF8-decrypted-archives.csv",std::ios::binary);
        std::ofstream ecsv(out/"RPF8-decrypted-entries.csv",std::ios::binary);
        acsv<<"archive,tag,platform,declared_entries,parsed_entries,bounds_valid,bounds_invalid,validation_ratio,status\n";
        ecsv<<"archive,index,hash_hex,extension,generated_name,enc_config,enc_key_id,entry_encrypted,is_resource,signature_protected,is_directory,compressor,byte_offset,on_disk_size,end_offset,logical_size,virtual_flags_hex,physical_flags_hex,offset_in_bounds,range_in_bounds,raw_magic_at_offset\n";
        uint64_t totalParsed=0,totalValid=0,totalInvalid=0;

        for(auto& a:archives){
            auto toc=ReadBytes(a.full,TOC_OFFSET,(size_t)a.entryCount*ENTRY_SIZE);
            if(a.tag!=0xFF)DecryptCbc(ctx,keys.at(a.tag),iv,toc);
            uint64_t good=0,bad=0; char platform=(char)(a.platform&0xFF);
            for(uint32_t i=0;i<a.entryCount;i++){
                const uint8_t* p=toc.data()+i*24;uint64_t q0=U64LE(p),q8=U64LE(p+8),q10=U64LE(p+16);
                uint32_t hash=(uint32_t)q0;uint8_t encCfg=(uint8_t)(q0>>32),encKey=(uint8_t)(q0>>40),extId=(uint8_t)(q0>>48);
                bool resource=((q0>>56)&1)!=0,sig=((q0>>57)&1)!=0,dir=extId==0xFE;
                uint64_t disk=(q8&0x0FFFFFFFull)<<4,off=((q8>>28)&0x7FFFFFFFull)<<4,end=off+disk;uint8_t comp=(uint8_t)(q8>>59);
                uint32_t vf=(uint32_t)q10,pf=(uint32_t)(q10>>32);
                uint64_t logical=dir?0:(resource?((q10&0xFFFFFFF0ull)+((q10>>32)&0xFFFFFFF0ull)):q10);
                bool ook=off<a.size,rok=ook&&end<=a.size;bool valid=ook&&(dir||disk==0||rok);
                if(valid)good++;else bad++;
                std::string ext=ExtFor(extId,platform);std::ostringstream hn;hn<<std::uppercase<<std::hex<<std::setw(8)<<std::setfill('0')<<hash;
                ecsv<<Csv(a.rel)<<","<<i<<","<<Hex(hash,8)<<","<<ext<<","<<hn.str()<<"."<<ext<<","<<(int)encCfg<<","<<(int)encKey<<","<<(encKey!=0xFF?"true":"false")<<","<<(resource?"true":"false")<<","<<(sig?"true":"false")<<","<<(dir?"true":"false")<<","<<(int)comp<<","<<off<<","<<disk<<","<<end<<","<<logical<<","<<(resource?Hex(vf,8):"")<<","<<(resource?Hex(pf,8):"")<<","<<(ook?"true":"false")<<","<<(rok?"true":"false")<<","<<Csv(RawMagic(a.full,off,a.size))<<"\n";
            }
            double ratio=a.entryCount?((double)good/a.entryCount):1.0;std::string status=ratio>=0.95?"decrypted_validated":"decrypted_suspicious";
            acsv<<Csv(a.rel)<<","<<Hex(a.tag,4)<<","<<Hex(a.platform,4)<<","<<a.entryCount<<","<<a.entryCount<<","<<good<<","<<bad<<","<<std::fixed<<std::setprecision(6)<<ratio<<","<<status<<"\n";
            std::cout<<"[TOC] "<<a.rel<<" -> "<<status<<" ("<<good<<"/"<<a.entryCount<<" bounds-valid)\n";
            totalParsed+=a.entryCount;totalValid+=good;totalInvalid+=bad;
        }
        acsv.close();ecsv.close();

        std::ofstream rep(out/"TFIT2-report.txt",std::ios::binary);
        rep<<TOOL<<" v"<<VERSION<<"\n";
        rep<<"Mode: READ-ONLY / metadata only\n";
        rep<<"RDR2 PID: "<<pid<<"\n";
        rep<<"Required secret blocks: "<<specs.size()<<" | found: "<<FoundCount(specs)<<"\n";
        rep<<"Raw secrets written: NO\n";
        rep<<"Archives decrypted: "<<archives.size()<<"\n";
        rep<<"Entries parsed: "<<totalParsed<<" | bounds valid: "<<totalValid<<" | invalid: "<<totalInvalid<<"\n";
        rep<<"Output contains hashes/types/offsets/sizes/flags only. No Rockstar asset was extracted.\n";
        rep.close();

        std::cout<<"\n[OK] Metadata catalog complete. Output: "<<out.string()<<"\n";
        return totalInvalid?4:0;
    } catch(const std::exception& e) {
        std::cerr<<"[ERROR] "<<e.what()<<"\n"; return 1;
    }
}
