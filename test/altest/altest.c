/* Проверка приёмов работы со звуком на настоящих библиотеках игры.
   Программа не запускает игру: она берёт OpenAL32.dll и ALURE32.dll рядом
   с собой и повторяет на них то, что делает наш патч. */
#include <windows.h>
#include <stdio.h>

typedef unsigned int ALuint;
typedef int ALint;
typedef int ALenum;
typedef float ALfloat;
typedef char ALchar;

#define AL_SOURCE_STATE 0x1010
#define AL_PLAYING      0x1012
#define AL_PAUSED       0x1013
#define AL_STOPPED      0x1014
#define AL_SEC_OFFSET   0x1024
#define AL_SAMPLE_OFFSET 0x1025
#define AL_GAIN         0x100A
#define AL_BUFFER       0x1009
#define AL_NO_ERROR     0

static void (*alGenSources)(int, ALuint*);
static void (*alSourcei)(ALuint, ALenum, ALint);
static void (*alSourcef)(ALuint, ALenum, ALfloat);
static void (*alGetSourcei)(ALuint, ALenum, ALint*);
static void (*alSourcePlay)(ALuint);
static void (*alSourceStop)(ALuint);
static void (*alSourceRewind)(ALuint);
static ALenum (*alGetError)(void);

static int (*alureInitDevice)(const ALchar*, const int*);
static ALuint (*alureCreateBufferFromFile)(const ALchar*);
static int (*alurePauseSource)(ALuint);
static int (*alureResumeSource)(ALuint);
static int (*alureUpdate)(void);
static void (*alureSleep)(ALfloat);
static const ALchar* (*alureGetErrorString)(void);

static const char* state_name(ALint s)
{
    if (s == AL_PLAYING) return "играет";
    if (s == AL_PAUSED)  return "на паузе";
    if (s == AL_STOPPED) return "остановлен";
    return "в исходном";
}

static void show(const char* what, ALuint src)
{
    ALint st = 0, samp = 0;
    alGetSourcei(src, AL_SOURCE_STATE, &st);
    alGetSourcei(src, AL_SAMPLE_OFFSET, &samp);
    printf("  %-34s состояние %-12s позиция %d, ошибка %d\n",
           what, state_name(st), samp, alGetError());
}

static ALuint g_src;
static DWORD WINAPI other_thread(LPVOID p)
{
    Sleep(200);
    printf("  из другого потока: ставим паузу средствами ALURE\n");
    int ok = alurePauseSource(g_src);
    printf("    ответ %d, ошибка OpenAL %d\n", ok, alGetError());
    Sleep(300);
    printf("  из другого потока: снимаем паузу\n");
    ok = alureResumeSource(g_src);
    printf("    ответ %d, ошибка OpenAL %d\n", ok, alGetError());
    return 0;
}

int main(int argc, char** argv)
{
    HMODULE al = LoadLibraryA("OpenAL32.dll");
    HMODULE alure = LoadLibraryA("ALURE32.dll");
    if (!al || !alure) { printf("библиотеки не загрузились\n"); return 1; }

    alGenSources = (void*)GetProcAddress(al, "alGenSources");
    alSourcei = (void*)GetProcAddress(al, "alSourcei");
    alSourcef = (void*)GetProcAddress(al, "alSourcef");
    alGetSourcei = (void*)GetProcAddress(al, "alGetSourcei");
    alSourcePlay = (void*)GetProcAddress(al, "alSourcePlay");
    alSourceStop = (void*)GetProcAddress(al, "alSourceStop");
    alSourceRewind = (void*)GetProcAddress(al, "alSourceRewind");
    alGetError = (void*)GetProcAddress(al, "alGetError");

    alureInitDevice = (void*)GetProcAddress(alure, "alureInitDevice");
    alureCreateBufferFromFile = (void*)GetProcAddress(alure, "alureCreateBufferFromFile");
    alurePauseSource = (void*)GetProcAddress(alure, "alurePauseSource");
    alureResumeSource = (void*)GetProcAddress(alure, "alureResumeSource");
    alureUpdate = (void*)GetProcAddress(alure, "alureUpdate");
    alureSleep = (void*)GetProcAddress(alure, "alureSleep");
    alureGetErrorString = (void*)GetProcAddress(alure, "alureGetErrorString");

    if (!alureInitDevice(NULL, NULL)) {
        printf("устройство не открылось: %s\n", alureGetErrorString());
        return 1;
    }
    printf("устройство открыто\n");

    const char* path = argc > 1 ? argv[1] : "music.mp3";
    ALuint buf = alureCreateBufferFromFile(path);
    if (!buf) { printf("трек %s не распаковался: %s\n", path, alureGetErrorString()); return 1; }
    printf("трек %s распакован\n\n", path);

    alGenSources(1, &g_src);
    alSourcei(g_src, AL_BUFFER, (ALint)buf);

    printf("1. Запуск и перемотка тем же приёмом, что зацикливает игра\n");
    alSourcePlay(g_src);
    Sleep(400);
    show("после 400 мс игры", g_src);
    alSourceRewind(g_src);
    alSourcePlay(g_src);
    Sleep(100);
    show("после перемотки и запуска", g_src);

    printf("\n2. Задание позиции на играющем источнике\n");
    alSourcef(g_src, AL_SEC_OFFSET, 0.0f);
    show("позиция задана в ноль", g_src);
    alSourcef(g_src, AL_SEC_OFFSET, 2.0f);
    show("позиция задана в две секунды", g_src);

    printf("\n3. Приглушение и возврат звука\n");
    alSourcef(g_src, AL_GAIN, 0.0f);
    show("громкость в ноль", g_src);
    alSourcef(g_src, AL_GAIN, 1.0f);
    show("громкость обратно", g_src);

    printf("\n4. Отложенный запуск: источник заряжен, но не пущен\n");
    alSourceStop(g_src);
    alSourceRewind(g_src);
    show("после остановки и перемотки", g_src);
    Sleep(300);
    show("через 300 мс без запуска", g_src);
    alSourcePlay(g_src);
    Sleep(100);
    show("после запуска", g_src);

    printf("\n5. Пауза средствами ALURE из другого потока\n");
    HANDLE th = CreateThread(NULL, 0, other_thread, NULL, 0, NULL);
    for (int i = 0; i < 12; i++) { alureUpdate(); Sleep(50); }
    WaitForSingleObject(th, 2000);
    show("после возврата из паузы", g_src);

    printf("\nготово\n");
    return 0;
}
