// Test only: exact pinned DrvUI_x64_ELGIN.dll 2.7.2.0, no printer/hardware.
#define _UNICODE
#include <windows.h>
#include <stdio.h>
#include <string.h>
typedef int (WINAPI *Merge)(HANDLE,void*,void*,void*,void*,int,void*,int);
int wmain(int n,wchar_t**a){if(n<3)return 1;HMODULE lib=LoadLibraryW(a[1]);if(!lib)return 2;unsigned char xpd[16384]={0},in[4688]={0},out[4688]={0};wchar_t path[2048];wcscpy(path,a[1]);wcscpy(wcsrchr(path,L'\\')+1,L"I9.xpd");FILE*f=_wfopen(path,L"rb");fread(xpd,1,sizeof(xpd),f);fclose(f);f=_wfopen(a[2],L"rb");fread(in,1,sizeof(in),f);fclose(f);memset(xpd,0,sizeof(xpd));*(int*)(xpd+0xa0)=1000000;*(int*)(xpd+0xa4)=1000000;memcpy(out,(unsigned char*)lib+0x4f20,4688);memcpy(in+220,out+220,8);int r=((Merge)((unsigned char*)lib+0x26fcc))(NULL,xpd,in,out,NULL,0,NULL,0);printf("merge=%d\n",r);for(int i=64;i<4688;i++)if(in[i]!=out[i])printf("offset=%x in=%02x out=%02x\n",i,in[i],out[i]);wcscpy(path,a[2]);wcscat(path,L".normalized");f=_wfopen(path,L"wb");fwrite(out,1,4688,f);fclose(f);int ok=*(short*)(out+78)==256&&*(short*)(out+80)==1000&&*(short*)(out+82)==720&&out[102]==0&&*(int*)(out+0x33c)==8&&*(int*)(out+0x340)==1&&*(int*)(out+0x344)==0&&(*(int*)(out+0x3a0)&15)==4&&*(int*)(out+0x7c0)==2&&out[0x7c4]==48&&out[0x7c5]==48&&*(int*)(out+0x7c8)==2;printf("RESULT %s (vendor normalization, memory fixture only)\n",ok?"PASS":"FAIL");return ok?0:5;}
