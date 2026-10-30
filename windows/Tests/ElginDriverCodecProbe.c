#define _UNICODE
#include <windows.h>
#include <commctrl.h>
#include <stdio.h>
#include <stdint.h>
#include <string.h>

typedef void (WINAPI *Helper)(HWND,void*,int);
typedef void (WINAPI *SaveHelper)(HWND,void*);
static INT_PTR CALLBACK noop(HWND w,UINT m,WPARAM wp,LPARAM lp){return FALSE;}
static void showcombo(HWND page,int id){HWND c=GetDlgItem(page,id); int index=(int)SendMessage(c,CB_GETCURSEL,0,0); wchar_t text[256]={0}; if(index>=0)SendMessage(c,CB_GETLBTEXT,index,(LPARAM)text);wprintf(L"control=%d index=%d data=%lld text=%ls\n",id,index,(long long)SendMessage(c,CB_GETITEMDATA,index,0),text);}
int wmain(int argc,wchar_t **argv){
 if(argc<3)return 2; HMODULE lib=LoadLibraryW(argv[1]); if(!lib){printf("Load error %lu\n",GetLastError());return 3;}
 typedef BOOL (WINAPI *ConvertFn)(HANDLE,DEVMODEW*,DEVMODEW*,LONG*,DWORD); LONG required=0; ((ConvertFn)GetProcAddress(lib,"DrvConvertDevMode"))(NULL,NULL,NULL,&required,4);printf("vendor DEVMODE required=%ld\n",required);if(required!=4688)return 11;
 INITCOMMONCONTROLSEX init={sizeof(init),ICC_WIN95_CLASSES};InitCommonControlsEx(&init);
 wchar_t xpdPath[2048]; wcscpy(xpdPath,argv[1]); wchar_t *last=wcsrchr(xpdPath,L'\\'); if(!last)return 7;wcscpy(last+1,L"I9.xpd"); FILE *f=_wfopen(xpdPath,L"rb");if(!f)return 8;unsigned char xpd[16384]={0};fread(xpd,1,sizeof(xpd),f);fclose(f);
 unsigned char ctx[0x1400]={0}; *(void**)(ctx+8)=xpd; *(int*)(ctx+0x1264)=0; // English, all data in this fixture only.
 HWND parent=CreateWindowExW(0,L"STATIC",L"RBT fixture",WS_OVERLAPPEDWINDOW,0,0,800,600,NULL,NULL,GetModuleHandle(NULL),NULL);
 HWND cut=CreateDialogParamW(lib,MAKEINTRESOURCEW(116),parent,noop,0); HWND logo=CreateDialogParamW(lib,MAKEINTRESOURCEW(121),parent,noop,0);if(!cut||!logo){printf("Dialog error %lu\n",GetLastError());return 4;}
 printf("Loaded supplied UI DLL, hidden resource dialogs; no printer registration or hardware access.\n");
 FILE *fixture=_wfopen(argv[2],L"rb");if(!fixture)return 9; if(fread(ctx+0x3c,1,4688,fixture)!=4688)return 10;fclose(fixture);*(int*)(ctx+0x1264)=argc>3?5:0;
 // Render the production C# codec output through the actual vendor property pages.
 ((Helper)((unsigned char*)lib+0x181a4))(cut,ctx,1);
 ((Helper)((unsigned char*)lib+0x17de8))(cut,ctx,1);((Helper)((unsigned char*)lib+0x17fe8))(cut,ctx,1);((Helper)((unsigned char*)lib+0x180b0))(cut,ctx,1);
 showcombo(cut,1087);showcombo(cut,1088);wchar_t distance[64];GetDlgItemTextW(cut,1089,distance,64);wprintf(L"cut distance=%ls\n",distance);
 unsigned char *slot=ctx+0x3e0+0x214;
 ((Helper)((unsigned char*)lib+0x1bea4))(logo,ctx,1);((Helper)((unsigned char*)lib+0x1bc20))(logo,ctx,1);
 SendDlgItemMessageW(logo,1126,CB_SETCURSEL,1,0);
 ((Helper)((unsigned char*)lib+0x1aea8))(logo,ctx,1);((Helper)((unsigned char*)lib+0x1b50c))(logo,ctx,1);((Helper)((unsigned char*)lib+0x1b79c))(logo,ctx,1);((Helper)((unsigned char*)lib+0x1b970))(logo,ctx,1);
 showcombo(logo,1126);showcombo(logo,1128);showcombo(logo,1124);showcombo(logo,1125);
 printf("logo center=%lld\n",(long long)SendDlgItemMessage(logo,1132,BM_GETCHECK,0,0));
 // Let the genuine UI save its controls and compare resulting settings.
 ((Helper)((unsigned char*)lib+0x17de8))(cut,ctx,0);((Helper)((unsigned char*)lib+0x17fe8))(cut,ctx,0);((Helper)((unsigned char*)lib+0x180b0))(cut,ctx,0);
 ((SaveHelper)((unsigned char*)lib+0x1c41c))(logo,ctx);
 printf("saved-cut when=%d mode=%d distance=%d;logo flags=%d mode=%d keys=%u,%u pos=%d\n",*(int*)(ctx+0x378),*(int*)(ctx+0x37c),*(int*)(ctx+0x380),*(int*)(ctx+0x3dc),*(int*)(slot+0x208),slot[0x20c],slot[0x20d],*(int*)(slot+0x210));
 int ok=*(int*)(ctx+0x378)==8&&*(int*)(ctx+0x37c)==1&&*(int*)(ctx+0x380)==0&&(*(int*)(ctx+0x3dc)&15)==4&&*(int*)(slot+0x208)==2&&slot[0x20c]==48&&slot[0x20d]==48&&*(int*)(slot+0x210)==2;
 printf("RESULT %s (production codec -> vendor UI -> vendor save)\n",ok?"PASS":"FAIL");DestroyWindow(cut);DestroyWindow(logo);FreeLibrary(lib);return ok?0:5;
}
