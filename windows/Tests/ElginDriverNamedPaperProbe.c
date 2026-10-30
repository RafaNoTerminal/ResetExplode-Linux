// TEST ONLY: exact supplied DrvUI_x64_ELGIN.dll 2.7.2.0.
// Mocks GetPrinterData in this process; never opens/registers a printer or sends hardware.
#define _UNICODE
#include <windows.h>
#include <commctrl.h>
#include <stdio.h>
#include <string.h>
#include <stdint.h>
typedef void (WINAPI *Helper)(HWND,void*,int);
typedef void (WINAPI *Save)(HWND,void*);
typedef int (WINAPI *Merge)(HANDLE,void*,void*,void*,void*,int,void*,int);
typedef BOOL (WINAPI *Forms)(HANDLE,void*,void**,void**,int*);
static unsigned char formData[92];
static DWORD WINAPI fakeData(HANDLE p,LPWSTR name,LPDWORD type,LPBYTE out,DWORD size,LPDWORD needed) {
 if(wcscmp(name,L"UserForm"))return ERROR_FILE_NOT_FOUND;
 if(type)*type=REG_BINARY;*needed=sizeof(formData);
 if(size<sizeof(formData))return ERROR_MORE_DATA;
 memcpy(out,formData,sizeof(formData));return ERROR_SUCCESS;
}
static int hook(HMODULE module) {
 unsigned char*base=(unsigned char*)module;
 IMAGE_NT_HEADERS64*nt=(void*)(base+((IMAGE_DOS_HEADER*)base)->e_lfanew);
 IMAGE_IMPORT_DESCRIPTOR*d=(void*)(base+nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress);
 for(;d->Name;d++) {
  IMAGE_THUNK_DATA64*n=(void*)(base+d->OriginalFirstThunk),*t=(void*)(base+d->FirstThunk);
  for(;n->u1.AddressOfData;n++,t++) {
   if(IMAGE_SNAP_BY_ORDINAL64(n->u1.Ordinal))continue;
   IMAGE_IMPORT_BY_NAME*entry=(void*)(base+n->u1.AddressOfData);
   if(!strcmp((char*)entry->Name,"GetPrinterDataW")) {
    DWORD previous;VirtualProtect(&t->u1.Function,sizeof(void*),PAGE_READWRITE,&previous);
    t->u1.Function=(ULONGLONG)(uintptr_t)fakeData;VirtualProtect(&t->u1.Function,sizeof(void*),previous,&previous);return 1;
   }
  }
 }
 return 0;
}
static INT_PTR CALLBACK noop(HWND h,UINT m,WPARAM w,LPARAM l){return FALSE;}
static void combo(HWND page,int id) {
 HWND h=GetDlgItem(page,id);int index=(int)SendMessage(h,CB_GETCURSEL,0,0);wchar_t name[256]={0};
 if(index>=0)SendMessage(h,CB_GETLBTEXT,index,(LPARAM)name);
 wprintf(L"control=%d index=%d data=%lld text=%ls\n",id,index,(long long)SendMessage(h,CB_GETITEMDATA,index,0),name);
}
int wmain(int argc,wchar_t**argv) {
 if(argc<3)return 1;HMODULE lib=LoadLibraryW(argv[1]);if(!lib||!hook(lib))return 2;
 unsigned char mode[4688],ctx[0x1400]={0};wchar_t file[2048];
 FILE*f=_wfopen(argv[2],L"rb");if(!f||fread(mode,1,sizeof(mode),f)!=sizeof(mode))return 3;fclose(f);
 wcscpy(file,argv[2]);wcscat(file,L".forms");f=_wfopen(file,L"rb");if(!f||fread(formData,1,92,f)!=92)return 4;fclose(f);
 void*raw=NULL,*forms=NULL;int count=0;
 if(!((Forms)((unsigned char*)lib+0x28034))((HANDLE)0x1234,NULL,&raw,&forms,&count)||count!=1)return 5;
 // Vendor parsed production UserForm, including pointers and dimensions.
 unsigned char builtin[40]={0};*(wchar_t**)(builtin+8)=L"Custom";*(int*)(builtin+16)=72000;*(int*)(builtin+20)=297000;
 *(void**)(ctx+0x18)=builtin;*(int*)(ctx+0x20)=1;
 *(void**)(ctx+0x28)=raw;*(void**)(ctx+0x30)=forms;*(int*)(ctx+0x38)=count;
 unsigned char metadata[4096]={0},normalized[4688];
 *(int*)(metadata+0xa0)=1000000;*(int*)(metadata+0xa4)=1000000;
 memcpy(normalized,(unsigned char*)lib+0x4f20,4688);
 memcpy(mode+220,normalized+220,8); // valid private-format signature for a synthetic mode
 ((Merge)((unsigned char*)lib+0x26fcc))(NULL,metadata,mode,normalized,builtin,1,forms,count);
 if(*(short*)(normalized+78)!=257||*(short*)(normalized+80)!=1000||*(short*)(normalized+82)!=720||wcscmp((wchar_t*)(normalized+102),L"Comprovantes"))return 7;
 memcpy(ctx+0x3c,normalized,4688);*(int*)(ctx+0x1264)=argc>3?5:0;
 INITCOMMONCONTROLSEX common={sizeof(common),ICC_WIN95_CLASSES};InitCommonControlsEx(&common);
 HWND parent=CreateWindowW(L"STATIC",L"RBT memory fixture",WS_OVERLAPPEDWINDOW,0,0,800,600,NULL,NULL,NULL,NULL);
 HWND paper=CreateDialogParamW(lib,MAKEINTRESOURCEW(101),parent,noop,0);
 HWND logo=CreateDialogParamW(lib,MAKEINTRESOURCEW(121),parent,noop,0);
 ((Helper)((unsigned char*)lib+0x1445c))(paper,ctx,1);combo(paper,1006);
 wchar_t selected[256]={0};GetDlgItemTextW(paper,1006,selected,256);
 int ok=!wcscmp(selected,L"Comprovantes")&&*(int*)((unsigned char*)forms+16)==72000&&*(int*)((unsigned char*)forms+20)==100000;
 ((Helper)((unsigned char*)lib+0x1445c))(paper,ctx,0);
 ok=ok&&*(short*)(ctx+0x3c+78)==257&&!wcscmp((wchar_t*)(ctx+0x3c+102),L"Comprovantes");
 // Opening this OEM page always shows Start Document first: this is a view filter.
 ((Helper)((unsigned char*)lib+0x1bc20))(logo,ctx,1);((Helper)((unsigned char*)lib+0x1bea4))(logo,ctx,1);
 printf("Initial OEM view (not the configured logo event):\n");combo(logo,1126);combo(logo,1128);
 ok=ok&&SendDlgItemMessageW(logo,1126,CB_GETCURSEL,0,0)==0&&SendDlgItemMessageW(logo,1128,CB_GETCURSEL,0,0)==0;
 SendDlgItemMessageW(logo,1126,CB_SETCURSEL,1,0);
 ((Helper)((unsigned char*)lib+0x1aea8))(logo,ctx,1);((Helper)((unsigned char*)lib+0x1b50c))(logo,ctx,1);((Helper)((unsigned char*)lib+0x1b79c))(logo,ctx,1);((Helper)((unsigned char*)lib+0x1b970))(logo,ctx,1);
 printf("Configured Start Page event after loading saved mode:\n");combo(logo,1126);combo(logo,1128);combo(logo,1124);combo(logo,1125);
 ok=ok&&SendDlgItemMessageW(logo,1128,CB_GETCURSEL,0,0)==2&&SendDlgItemMessageW(logo,1132,BM_GETCHECK,0,0)==BST_CHECKED;
 ((Save)((unsigned char*)lib+0x1c41c))(logo,ctx);
 ok=ok&&(*(int*)(ctx+0x3c+0x3a0)&15)==4&&*(int*)(ctx+0x3c+0x7c0)==2&&ctx[0x3c+0x7c4]==48&&ctx[0x3c+0x7c5]==48;
 printf("RESULT %s (production UserForm + named paper + logo -> vendor load/save, fixture only)\n",ok?"PASS":"FAIL");
 DestroyWindow(paper);DestroyWindow(logo);DestroyWindow(parent);FreeLibrary(lib);return ok?0:6;
}
