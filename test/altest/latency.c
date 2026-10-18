/* Умеет ли библиотека игры сообщать задержку вывода?
   Если да, её можно спрашивать вместо зашитого числа. */
#include <windows.h>
#include <stdio.h>
typedef unsigned int ALuint; typedef int ALint, ALenum; typedef char ALchar;
typedef double ALdouble;
#define AL_BUFFER 0x1009
#define AL_SEC_OFFSET_LATENCY_SOFT 0x1201
#define AL_SAMPLE_OFFSET 0x1025

static void (*alGenSources)(int, ALuint*);
static void (*alSourcei)(ALuint, ALenum, ALint);
static void (*alGetSourcei)(ALuint, ALenum, ALint*);
static void (*alSourcePlay)(ALuint);
static void* (*alGetProcAddress)(const ALchar*);
static int (*alIsExtensionPresent)(const ALchar*);
static ALenum (*alGetError)(void);
static int (*alureInitDevice)(const ALchar*, const int*);
static ALuint (*alureCreateBufferFromFile)(const ALchar*);
static void (*alGetSourcedvSOFT)(ALuint, ALenum, ALdouble*);

int main(int argc, char** argv)
{
    HMODULE al=LoadLibraryA("OpenAL32.dll"), alure=LoadLibraryA("ALURE32.dll");
    alGenSources=(void*)GetProcAddress(al,"alGenSources");
    alSourcei=(void*)GetProcAddress(al,"alSourcei");
    alGetSourcei=(void*)GetProcAddress(al,"alGetSourcei");
    alSourcePlay=(void*)GetProcAddress(al,"alSourcePlay");
    alGetProcAddress=(void*)GetProcAddress(al,"alGetProcAddress");
    alIsExtensionPresent=(void*)GetProcAddress(al,"alIsExtensionPresent");
    alGetError=(void*)GetProcAddress(al,"alGetError");
    alureInitDevice=(void*)GetProcAddress(alure,"alureInitDevice");
    alureCreateBufferFromFile=(void*)GetProcAddress(alure,"alureCreateBufferFromFile");

    if (!alureInitDevice(NULL,NULL)) { printf("устройство не открылось\n"); return 1; }
    printf("расширение задержки: %s\n",
        alIsExtensionPresent("AL_SOFT_source_latency") ? "поддерживается" : "нет");
    alGetSourcedvSOFT=(void*)alGetProcAddress("alGetSourcedvSOFT");
    printf("функция запроса: %s\n", alGetSourcedvSOFT ? "нашлась" : "не нашлась");
    if (!alGetSourcedvSOFT) return 0;

    ALuint buf=alureCreateBufferFromFile(argc>1?argv[1]:"music.mp3");
    if (!buf) { printf("трек не распакован\n"); return 1; }
    ALuint src; alGenSources(1,&src); alSourcei(src, AL_BUFFER, (ALint)buf);
    alSourcePlay(src);
    for (int i=0;i<5;i++)
    {
        ALdouble v[2]={0,0};
        alGetSourcedvSOFT(src, AL_SEC_OFFSET_LATENCY_SOFT, v);
        ALint samp=0; alGetSourcei(src, AL_SAMPLE_OFFSET, &samp);
        printf("  проигранo %.3f с, задержка вывода %.1f мс, ошибка %d\n",
               v[0], v[1]*1000.0, alGetError());
        Sleep(60);
    }
    return 0;
}
