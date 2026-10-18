/* Прототип поиска функции так, как это будет делать патч.
   Патч не может звать GetProcAddress: игра её не импортирует. Зато у него
   есть адрес любой функции OpenAL из таблицы импорта игры. От него ищем
   начало библиотеки и читаем её таблицу имён, как делает загрузчик. */
#include <windows.h>
#include <stdio.h>

typedef unsigned int ALuint; typedef int ALint, ALenum; typedef char ALchar;
typedef double ALdouble;
#define AL_BUFFER 0x1009
#define AL_SEC_OFFSET_LATENCY_SOFT 0x1201

/* шаг 1: по адресу внутри библиотеки найти её начало.
   Проверка доступности не нужна: идя вниз шагами по 64 КБ от адреса
   внутри библиотеки, мы упрёмся в её заголовок раньше, чем выйдем
   за отображённую память. */
static BYTE* module_base(void* any_fn)
{
    BYTE* p = (BYTE*)((UINT_PTR)any_fn & ~(UINT_PTR)0xFFFF);
    for (int i = 0; i < 4096; i++, p -= 0x10000)
    {
        if (p[0] != 'M' || p[1] != 'Z') continue;
        LONG e = *(LONG*)(p + 0x3C);
        if (e <= 0 || e > 0x1000) continue;
        if (*(DWORD*)(p + e) != 0x00004550) continue;   /* "PE\0\0" */
        return p;
    }
    return NULL;
}

/* шаг 2: найти в таблице имён нужную функцию */
static void* find_export(BYTE* base, const char* want)
{
    LONG e = *(LONG*)(base + 0x3C);
    BYTE* nt = base + e;
    DWORD dir = *(DWORD*)(nt + 24 + 96);            /* адрес таблицы экспорта */
    if (!dir) return NULL;
    BYTE* ex = base + dir;
    DWORD count = *(DWORD*)(ex + 24);
    DWORD names = *(DWORD*)(ex + 32);
    DWORD ords  = *(DWORD*)(ex + 36);
    DWORD funcs = *(DWORD*)(ex + 28);
    for (DWORD i = 0; i < count; i++)
    {
        const char* nm = (const char*)(base + ((DWORD*)(base + names))[i]);
        const char* a = nm; const char* b = want;
        while (*a && *a == *b) { a++; b++; }
        if (*a || *b) continue;
        WORD ord = ((WORD*)(base + ords))[i];
        return base + ((DWORD*)(base + funcs))[ord];
    }
    return NULL;
}

int main(int argc, char** argv)
{
    HMODULE al = LoadLibraryA("OpenAL32.dll"), alure = LoadLibraryA("ALURE32.dll");
    void* known = GetProcAddress(al, "alSourcePlay");   /* это и лежит в таблице игры */
    printf("известная функция alSourcePlay: %p\n", known);

    BYTE* base = module_base(known);
    printf("начало библиотеки найдено: %p, оно же по мнению Windows: %p, совпало: %s\n",
           base, (void*)al, base == (BYTE*)al ? "да" : "НЕТ");
    if (!base) return 1;

    void* getdv = find_export(base, "alGetSourcedvSOFT");
    void* real  = GetProcAddress(al, "alGetSourcedvSOFT");
    printf("alGetSourcedvSOFT нашли: %p, у Windows: %p, совпало: %s\n",
           getdv, real, getdv == real ? "да" : "НЕТ");
    if (!getdv) return 1;

    /* и проверим, что найденная функция работает */
    void (*alGenSources)(int, ALuint*) = (void*)GetProcAddress(al, "alGenSources");
    void (*alSourcei)(ALuint, ALenum, ALint) = (void*)GetProcAddress(al, "alSourcei");
    void (*alSourcePlay)(ALuint) = (void*)known;
    int (*alureInitDevice)(const ALchar*, const int*) = (void*)GetProcAddress(alure, "alureInitDevice");
    ALuint (*alureCreateBufferFromFile)(const ALchar*) = (void*)GetProcAddress(alure, "alureCreateBufferFromFile");
    void (*getdvfn)(ALuint, ALenum, ALdouble*) = (void*)getdv;

    if (!alureInitDevice(NULL, NULL)) { printf("устройство не открылось\n"); return 1; }
    ALuint buf = alureCreateBufferFromFile(argc > 1 ? argv[1] : "music.mp3");
    if (!buf) { printf("трек не распакован\n"); return 1; }
    ALuint src; alGenSources(1, &src); alSourcei(src, AL_BUFFER, (ALint)buf);
    alSourcePlay(src);
    ALdouble v[2] = {0, 0};
    getdvfn(src, AL_SEC_OFFSET_LATENCY_SOFT, v);
    printf("через найденную функцию: проиграно %.3f с, задержка вывода %.1f мс\n", v[0], v[1] * 1000.0);
    return 0;
}
