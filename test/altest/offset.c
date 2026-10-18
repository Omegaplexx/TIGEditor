/* Растёт ли позиция источника сразу после запуска, или сначала стоит?
   Если стоит, а потом трогается, это и есть задержка устройства,
   и её можно ждать вместо зашитого числа. */
#include <windows.h>
#include <stdio.h>

typedef unsigned int ALuint;
typedef int ALint, ALenum;
typedef float ALfloat;
typedef char ALchar;
#define AL_SOURCE_STATE 0x1010
#define AL_SAMPLE_OFFSET 0x1025
#define AL_BUFFER 0x1009
#define AL_PLAYING 0x1012

static void (*alGenSources)(int, ALuint*);
static void (*alSourcei)(ALuint, ALenum, ALint);
static void (*alGetSourcei)(ALuint, ALenum, ALint*);
static void (*alSourcePlay)(ALuint);
static void (*alSourceStop)(ALuint);
static void (*alSourceRewind)(ALuint);
static int (*alureInitDevice)(const ALchar*, const int*);
static ALuint (*alureCreateBufferFromFile)(const ALchar*);
static const ALchar* (*alureGetErrorString)(void);

int main(int argc, char** argv)
{
    HMODULE al = LoadLibraryA("OpenAL32.dll"), alure = LoadLibraryA("ALURE32.dll");
    alGenSources=(void*)GetProcAddress(al,"alGenSources");
    alSourcei=(void*)GetProcAddress(al,"alSourcei");
    alGetSourcei=(void*)GetProcAddress(al,"alGetSourcei");
    alSourcePlay=(void*)GetProcAddress(al,"alSourcePlay");
    alSourceStop=(void*)GetProcAddress(al,"alSourceStop");
    alSourceRewind=(void*)GetProcAddress(al,"alSourceRewind");
    alureInitDevice=(void*)GetProcAddress(alure,"alureInitDevice");
    alureCreateBufferFromFile=(void*)GetProcAddress(alure,"alureCreateBufferFromFile");
    alureGetErrorString=(void*)GetProcAddress(alure,"alureGetErrorString");

    if (!alureInitDevice(NULL,NULL)) { printf("устройство не открылось\n"); return 1; }
    ALuint buf = alureCreateBufferFromFile(argc>1?argv[1]:"music.mp3");
    if (!buf) { printf("трек не распакован: %s\n", alureGetErrorString()); return 1; }

    ALuint src; alGenSources(1,&src); alSourcei(src, AL_BUFFER, (ALint)buf);

    for (int run = 0; run < 3; run++)
    {
        alSourceStop(src); alSourceRewind(src);
        LARGE_INTEGER f, a, b; QueryPerformanceFrequency(&f);
        QueryPerformanceCounter(&a);
        alSourcePlay(src);
        ALint samp = 0, st = 0, first = -1;
        for (int i = 0; i < 2000; i++)
        {
            alGetSourcei(src, AL_SAMPLE_OFFSET, &samp);
            alGetSourcei(src, AL_SOURCE_STATE, &st);
            if (samp > 0) { QueryPerformanceCounter(&b);
                first = (int)((b.QuadPart-a.QuadPart)*1000/f.QuadPart); break; }
            Sleep(1);
        }
        printf("прогон %d: позиция сдвинулась через %d мс, состояние %s\n",
               run+1, first, st==AL_PLAYING ? "играет" : "не играет");
        Sleep(300);
    }
    return 0;
}
