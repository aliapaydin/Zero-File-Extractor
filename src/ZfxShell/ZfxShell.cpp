// Zero File Extractor - Dosya Gezgini sağ tık menüsü.
//
// Windows 11'in yeni bağlam menüsü (ve Windows 10'un klasik menüsü) paketli uygulamalardan
// IExplorerCommand uygulayan bir COM sınıfı ister. Bu DLL "Zero File Extractor" alt menüsünü üretir;
// asıl işi yapmaz, yalnızca seçili yolları ZeroFileExtractor.exe'ye komut satırıyla iletir:
//
//     ZeroFileExtractor.exe --extract-here "C:\a.zip" "C:\b.rar"
//
// Komut satırı çok uzarsa yollar geçici bir liste dosyasına yazılır ve "@liste" olarak verilir.
// Komut adları Services/LaunchRequest.cs ile aynı olmalı.

#include <windows.h>
#include <shobjidl_core.h>
#include <shlwapi.h>
#include <new>
#include <string>
#include <vector>
#include <memory>

namespace
{
    HINSTANCE g_module = nullptr;
    long g_objects = 0;

    // {94EADFAF-24FB-4E59-8F3A-7AF287E8FF49} - Package.appxmanifest ile aynı
    const CLSID CLSID_ZfxMenu = { 0x94eadfaf, 0x24fb, 0x4e59, { 0x8f, 0x3a, 0x7a, 0xf2, 0x87, 0xe8, 0xff, 0x49 } };

    enum class Command
    {
        Open,
        ExtractHere,
        ExtractToFolder,
        ExtractAsk,
        Test,
        Separator,
        Zip,
        SevenZip,
        CompressAsk,
    };

    const Command kCommands[] =
    {
        Command::Open, Command::ExtractHere, Command::ExtractToFolder, Command::ExtractAsk, Command::Test,
        Command::Separator,
        Command::Zip, Command::SevenZip, Command::CompressAsk,
    };

    const wchar_t* Switch(Command command)
    {
        switch (command)
        {
        case Command::Open: return L"--open";
        case Command::ExtractHere: return L"--extract-here";
        case Command::ExtractToFolder: return L"--extract-to-folder";
        case Command::ExtractAsk: return L"--extract-ask";
        case Command::Test: return L"--test";
        case Command::Zip: return L"--zip";
        case Command::SevenZip: return L"--7z";
        case Command::CompressAsk: return L"--compress-ask";
        default: return L"";
        }
    }

    bool IsExtractCommand(Command command)
    {
        return command == Command::Open || command == Command::ExtractHere || command == Command::ExtractToFolder ||
               command == Command::ExtractAsk || command == Command::Test;
    }

    bool IsTurkish()
    {
        return PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_TURKISH;
    }

    std::wstring ToLower(std::wstring text)
    {
        if (!text.empty()) CharLowerBuffW(text.data(), static_cast<DWORD>(text.size()));
        return text;
    }

    bool EndsWith(const std::wstring& text, const wchar_t* suffix)
    {
        size_t length = wcslen(suffix);
        return text.size() >= length && text.compare(text.size() - length, length, suffix) == 0;
    }

    // Engine/ArchiveFormats.cs içindeki listeyle aynı tutulmalı.
    const wchar_t* const kArchiveExtensions[] =
    {
        L".zip", L".zipx", L".7z", L".rar", L".tar", L".gz", L".tgz", L".bz2", L".tbz2", L".tbz", L".xz", L".txz",
        L".zst", L".tzst", L".lz", L".tlz", L".arj", L".ace", L".arc", L".jar", L".war", L".apk", L".ipa", L".epub",
        L".cbz", L".cbr", L".nupkg", L".vsix", L".xpi", L".001", L".r00",
    };

    std::wstring FileName(const std::wstring& path)
    {
        size_t slash = path.find_last_of(L"\\/");
        return slash == std::wstring::npos ? path : path.substr(slash + 1);
    }

    bool IsDirectory(const std::wstring& path)
    {
        DWORD attributes = GetFileAttributesW(path.c_str());
        return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
    }

    bool IsArchive(const std::wstring& path)
    {
        std::wstring name = ToLower(FileName(path));
        for (const wchar_t* extension : kArchiveExtensions)
        {
            if (EndsWith(name, extension)) return !IsDirectory(path);
        }
        return false;
    }

    // "rapor.tar.gz" -> "rapor", "foto.zip" -> "foto", "notlar.txt" -> "notlar"
    std::wstring Stem(const std::wstring& fileName)
    {
        std::wstring lower = ToLower(fileName);
        size_t dot = lower.find_last_of(L'.');
        if (dot == std::wstring::npos || dot == 0) return fileName;
        std::wstring stem = fileName.substr(0, dot);
        if (EndsWith(ToLower(stem), L".tar") && stem.size() > 4) stem = stem.substr(0, stem.size() - 4);
        return stem;
    }

    std::wstring Shorten(const std::wstring& text)
    {
        const size_t limit = 36;
        if (text.size() <= limit) return text;
        return text.substr(0, limit - 1) + L"\x2026";
    }

    struct Selection
    {
        std::vector<std::wstring> paths;
        size_t archives = 0;

        bool Empty() const { return paths.empty(); }

        // Yeni arşiv için önerilen ad: tek öğede öğenin adı, çoklu seçimde bulunduğu klasörün adı.
        std::wstring ArchiveBaseName() const
        {
            if (paths.empty()) return L"Archive";
            if (paths.size() == 1)
            {
                std::wstring name = FileName(paths[0]);
                return IsDirectory(paths[0]) ? name : Stem(name);
            }
            std::wstring first = paths[0];
            size_t slash = first.find_last_of(L"\\/");
            if (slash == std::wstring::npos) return L"Archive";
            std::wstring parent = FileName(first.substr(0, slash));
            if (parent.empty() || parent.find(L':') != std::wstring::npos) return L"Archive";
            return parent;
        }
    };

    std::shared_ptr<Selection> ReadSelection(IShellItemArray* items)
    {
        auto selection = std::make_shared<Selection>();
        if (items == nullptr) return selection;
        DWORD count = 0;
        if (FAILED(items->GetCount(&count))) return selection;
        selection->paths.reserve(count);
        for (DWORD i = 0; i < count; i++)
        {
            IShellItem* item = nullptr;
            if (FAILED(items->GetItemAt(i, &item)) || item == nullptr) continue;
            PWSTR path = nullptr;
            if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)) && path != nullptr)
            {
                selection->paths.emplace_back(path);
                CoTaskMemFree(path);
            }
            item->Release();
        }
        // Menü hızlı açılsın: çok büyük seçimlerde yalnızca ilk öğelere bakılır.
        size_t probe = selection->paths.size() < 64 ? selection->paths.size() : 64;
        for (size_t i = 0; i < probe; i++)
        {
            if (IsArchive(selection->paths[i])) selection->archives++;
        }
        return selection;
    }

    std::wstring Title(Command command, const Selection& selection)
    {
        bool tr = IsTurkish();
        bool single = selection.paths.size() == 1;
        switch (command)
        {
        case Command::Open:
            return tr ? L"Zero File Extractor ile a\x00e7" : L"Open with Zero File Extractor";
        case Command::ExtractHere:
            return tr ? L"Buraya \x00e7\x0131kar" : L"Extract here";
        case Command::ExtractToFolder:
            if (single)
            {
                std::wstring folder = Shorten(Stem(FileName(selection.paths[0])));
                return tr ? L"\"" + folder + L"\\\" klas\x00f6r\x00fcne \x00e7\x0131kar" : L"Extract to \"" + folder + L"\\\"";
            }
            return tr ? L"Her ar\x015fivi kendi klas\x00f6r\x00fcne \x00e7\x0131kar" : L"Extract each to its own folder";
        case Command::ExtractAsk:
            return tr ? L"Klas\x00f6r se\x00e7" L"erek \x00e7\x0131kar\x2026" : L"Extract to\x2026";
        case Command::Test:
            return tr ? L"Ar\x015fivi test et" : L"Test archive";
        case Command::Zip:
        {
            std::wstring name = Shorten(selection.ArchiveBaseName()) + L".zip";
            return tr ? L"\"" + name + L"\" olarak s\x0131k\x0131\x015ft\x0131r" : L"Add to \"" + name + L"\"";
        }
        case Command::SevenZip:
        {
            std::wstring name = Shorten(selection.ArchiveBaseName()) + L".7z";
            return tr ? L"\"" + name + L"\" olarak s\x0131k\x0131\x015ft\x0131r" : L"Add to \"" + name + L"\"";
        }
        case Command::CompressAsk:
            return tr ? L"S\x0131k\x0131\x015ft\x0131r\x2026 (bi\x00e7im, d\x00fczey, parola)" : L"Compress\x2026 (format, level, password)";
        default:
            return L"";
        }
    }

    bool Visible(Command command, const Selection& selection)
    {
        if (selection.Empty()) return false;
        if (command == Command::Open) return selection.paths.size() == 1 && selection.archives == 1;
        if (IsExtractCommand(command) || command == Command::Separator) return selection.archives > 0;
        return true;
    }

    std::wstring ModuleDirectory()
    {
        std::wstring buffer(MAX_PATH, L'\0');
        for (;;)
        {
            DWORD length = GetModuleFileNameW(g_module, buffer.data(), static_cast<DWORD>(buffer.size()));
            if (length == 0) return L"";
            if (length < buffer.size())
            {
                buffer.resize(length);
                break;
            }
            buffer.resize(buffer.size() * 2);
        }
        size_t slash = buffer.find_last_of(L'\\');
        return slash == std::wstring::npos ? L"" : buffer.substr(0, slash + 1);
    }

    std::wstring ExePath()
    {
        return ModuleDirectory() + L"ZeroFileExtractor.exe";
    }

    // CommandLineToArgvW kurallarına göre tırnaklar: tırnaktan önceki ters eğik çizgiler ikilenir.
    std::wstring Quote(const std::wstring& argument)
    {
        std::wstring result = L"\"";
        size_t backslashes = 0;
        for (wchar_t c : argument)
        {
            if (c == L'\\')
            {
                backslashes++;
            }
            else if (c == L'"')
            {
                result.append(backslashes + 1, L'\\');
                backslashes = 0;
            }
            else
            {
                backslashes = 0;
            }
            result.push_back(c);
        }
        result.append(backslashes, L'\\');
        result.push_back(L'"');
        return result;
    }

    // Yolları UTF-16 (BOM'lu) bir liste dosyasına yazar; başarısızsa boş döner.
    std::wstring WriteListFile(const std::vector<std::wstring>& paths)
    {
        wchar_t folder[MAX_PATH + 1] = {};
        if (GetTempPathW(MAX_PATH, folder) == 0) return L"";
        wchar_t file[MAX_PATH + 1] = {};
        if (GetTempFileNameW(folder, L"zfx", 0, file) == 0) return L"";
        HANDLE handle = CreateFileW(file, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_TEMPORARY, nullptr);
        if (handle == INVALID_HANDLE_VALUE) return L"";
        std::wstring content = L"\xFEFF";
        for (const auto& path : paths)
        {
            content += path;
            content += L"\r\n";
        }
        DWORD written = 0;
        BOOL ok = WriteFile(handle, content.data(), static_cast<DWORD>(content.size() * sizeof(wchar_t)), &written, nullptr);
        CloseHandle(handle);
        if (!ok)
        {
            DeleteFileW(file);
            return L"";
        }
        return file;
    }

    HRESULT Launch(Command command, const Selection& selection)
    {
        if (selection.Empty()) return S_OK;
        std::wstring exe = ExePath();
        std::wstring commandLine = Quote(exe) + L" " + Switch(command);

        size_t total = commandLine.size();
        for (const auto& path : selection.paths) total += path.size() * 2 + 3;
        if (total < 30000)
        {
            for (const auto& path : selection.paths) commandLine += L" " + Quote(path);
        }
        else
        {
            std::wstring list = WriteListFile(selection.paths);
            if (list.empty()) return E_FAIL;
            commandLine += L" " + Quote(L"@" + list);
        }

        // Çalışma klasörü: ilk öğenin bulunduğu klasör.
        std::wstring directory = selection.paths[0];
        size_t slash = directory.find_last_of(L'\\');
        directory = slash == std::wstring::npos ? L"" : directory.substr(0, slash + 1);

        STARTUPINFOW startup = {};
        startup.cb = sizeof(startup);
        PROCESS_INFORMATION process = {};
        std::vector<wchar_t> buffer(commandLine.begin(), commandLine.end());
        buffer.push_back(L'\0');
        if (!CreateProcessW(exe.c_str(), buffer.data(), nullptr, nullptr, FALSE, 0, nullptr,
                            directory.empty() ? nullptr : directory.c_str(), &startup, &process))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return S_OK;
    }

    // ------------------------------------------------------------------ alt komut

    class SubCommand final : public IExplorerCommand
    {
    public:
        SubCommand(Command command, std::shared_ptr<Selection> cached)
            : _command(command), _cached(std::move(cached))
        {
            InterlockedIncrement(&g_objects);
        }

        IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
        {
            if (ppv == nullptr) return E_POINTER;
            if (riid == IID_IUnknown || riid == IID_IExplorerCommand)
            {
                *ppv = static_cast<IExplorerCommand*>(this);
                AddRef();
                return S_OK;
            }
            *ppv = nullptr;
            return E_NOINTERFACE;
        }
        IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_refs); }
        IFACEMETHODIMP_(ULONG) Release() override
        {
            long refs = InterlockedDecrement(&_refs);
            if (refs == 0) delete this;
            return refs;
        }

        IFACEMETHODIMP GetTitle(IShellItemArray* items, LPWSTR* name) override
        {
            if (name == nullptr) return E_POINTER;
            *name = nullptr;
            if (_command == Command::Separator) return S_FALSE;
            return SHStrDupW(Title(_command, *Resolve(items)).c_str(), name);
        }
        IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
        {
            if (icon != nullptr) *icon = nullptr;
            return E_NOTIMPL;
        }
        IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override
        {
            if (tip != nullptr) *tip = nullptr;
            return E_NOTIMPL;
        }
        IFACEMETHODIMP GetCanonicalName(GUID* name) override
        {
            if (name != nullptr) *name = GUID_NULL;
            return S_OK;
        }
        IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
        {
            if (state == nullptr) return E_POINTER;
            *state = Visible(_command, *Resolve(items)) ? ECS_ENABLED : ECS_HIDDEN;
            return S_OK;
        }
        IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
        {
            if (_command == Command::Separator) return S_OK;
            return Launch(_command, *Resolve(items));
        }
        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
        {
            if (flags == nullptr) return E_POINTER;
            *flags = _command == Command::Separator ? ECF_ISSEPARATOR : ECF_DEFAULT;
            return S_OK;
        }
        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
        {
            if (commands != nullptr) *commands = nullptr;
            return E_NOTIMPL;
        }

    private:
        ~SubCommand() { InterlockedDecrement(&g_objects); }

        // Gezgin alt komutlara seçimi her zaman vermez; verilmezse kök komutun gördüğü seçim kullanılır.
        std::shared_ptr<Selection> Resolve(IShellItemArray* items)
        {
            if (items != nullptr)
            {
                auto fresh = ReadSelection(items);
                if (!fresh->Empty()) _cached = fresh;
            }
            return _cached;
        }

        long _refs = 1;
        Command _command;
        std::shared_ptr<Selection> _cached;
    };

    class CommandEnumerator final : public IEnumExplorerCommand
    {
    public:
        CommandEnumerator(std::shared_ptr<Selection> selection, size_t index = 0)
            : _selection(std::move(selection)), _index(index)
        {
            InterlockedIncrement(&g_objects);
        }

        IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
        {
            if (ppv == nullptr) return E_POINTER;
            if (riid == IID_IUnknown || riid == IID_IEnumExplorerCommand)
            {
                *ppv = static_cast<IEnumExplorerCommand*>(this);
                AddRef();
                return S_OK;
            }
            *ppv = nullptr;
            return E_NOINTERFACE;
        }
        IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_refs); }
        IFACEMETHODIMP_(ULONG) Release() override
        {
            long refs = InterlockedDecrement(&_refs);
            if (refs == 0) delete this;
            return refs;
        }

        IFACEMETHODIMP Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) override
        {
            if (commands == nullptr) return E_POINTER;
            ULONG produced = 0;
            while (produced < count && _index < ARRAYSIZE(kCommands))
            {
                auto* command = new (std::nothrow) SubCommand(kCommands[_index], _selection);
                if (command == nullptr) break;
                commands[produced++] = command;
                _index++;
            }
            if (fetched != nullptr) *fetched = produced;
            return produced == count ? S_OK : S_FALSE;
        }
        IFACEMETHODIMP Skip(ULONG count) override
        {
            _index += count;
            if (_index > ARRAYSIZE(kCommands)) _index = ARRAYSIZE(kCommands);
            return S_OK;
        }
        IFACEMETHODIMP Reset() override
        {
            _index = 0;
            return S_OK;
        }
        IFACEMETHODIMP Clone(IEnumExplorerCommand** copy) override
        {
            if (copy == nullptr) return E_POINTER;
            *copy = new (std::nothrow) CommandEnumerator(_selection, _index);
            return *copy != nullptr ? S_OK : E_OUTOFMEMORY;
        }

    private:
        ~CommandEnumerator() { InterlockedDecrement(&g_objects); }

        long _refs = 1;
        std::shared_ptr<Selection> _selection;
        size_t _index;
    };

    // ------------------------------------------------------------------ kök komut ("Zero File Extractor >")

    class RootCommand final : public IExplorerCommand
    {
    public:
        RootCommand() : _selection(std::make_shared<Selection>())
        {
            InterlockedIncrement(&g_objects);
        }

        IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
        {
            if (ppv == nullptr) return E_POINTER;
            if (riid == IID_IUnknown || riid == IID_IExplorerCommand)
            {
                *ppv = static_cast<IExplorerCommand*>(this);
                AddRef();
                return S_OK;
            }
            *ppv = nullptr;
            return E_NOINTERFACE;
        }
        IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_refs); }
        IFACEMETHODIMP_(ULONG) Release() override
        {
            long refs = InterlockedDecrement(&_refs);
            if (refs == 0) delete this;
            return refs;
        }

        IFACEMETHODIMP GetTitle(IShellItemArray* items, LPWSTR* name) override
        {
            if (name == nullptr) return E_POINTER;
            Remember(items);
            return SHStrDupW(L"Zero File Extractor", name);
        }
        IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
        {
            if (icon == nullptr) return E_POINTER;
            std::wstring path = ExePath() + L",0";
            return SHStrDupW(path.c_str(), icon);
        }
        IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override
        {
            if (tip != nullptr) *tip = nullptr;
            return E_NOTIMPL;
        }
        IFACEMETHODIMP GetCanonicalName(GUID* name) override
        {
            if (name != nullptr) *name = CLSID_ZfxMenu;
            return S_OK;
        }
        IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
        {
            if (state == nullptr) return E_POINTER;
            Remember(items);
            *state = _selection->Empty() ? ECS_HIDDEN : ECS_ENABLED;
            return S_OK;
        }
        IFACEMETHODIMP Invoke(IShellItemArray*, IBindCtx*) override { return S_OK; }
        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
        {
            if (flags == nullptr) return E_POINTER;
            *flags = ECF_HASSUBCOMMANDS;
            return S_OK;
        }
        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
        {
            if (commands == nullptr) return E_POINTER;
            *commands = new (std::nothrow) CommandEnumerator(_selection);
            return *commands != nullptr ? S_OK : E_OUTOFMEMORY;
        }

    private:
        ~RootCommand() { InterlockedDecrement(&g_objects); }

        void Remember(IShellItemArray* items)
        {
            if (items == nullptr) return;
            auto fresh = ReadSelection(items);
            // Alt komutlar aynı nesneyi paylaşır; içeriği yerinde güncellenir.
            if (!fresh->Empty()) *_selection = *fresh;
        }

        long _refs = 1;
        std::shared_ptr<Selection> _selection;
    };

    class ClassFactory final : public IClassFactory
    {
    public:
        ClassFactory() { InterlockedIncrement(&g_objects); }

        IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
        {
            if (ppv == nullptr) return E_POINTER;
            if (riid == IID_IUnknown || riid == IID_IClassFactory)
            {
                *ppv = static_cast<IClassFactory*>(this);
                AddRef();
                return S_OK;
            }
            *ppv = nullptr;
            return E_NOINTERFACE;
        }
        IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_refs); }
        IFACEMETHODIMP_(ULONG) Release() override
        {
            long refs = InterlockedDecrement(&_refs);
            if (refs == 0) delete this;
            return refs;
        }

        IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override
        {
            if (ppv == nullptr) return E_POINTER;
            *ppv = nullptr;
            if (outer != nullptr) return CLASS_E_NOAGGREGATION;
            auto* command = new (std::nothrow) RootCommand();
            if (command == nullptr) return E_OUTOFMEMORY;
            HRESULT result = command->QueryInterface(riid, ppv);
            command->Release();
            return result;
        }
        IFACEMETHODIMP LockServer(BOOL lock) override
        {
            if (lock) InterlockedIncrement(&g_objects);
            else InterlockedDecrement(&g_objects);
            return S_OK;
        }

    private:
        ~ClassFactory() { InterlockedDecrement(&g_objects); }

        long _refs = 1;
    };
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, LPVOID* ppv)
{
    if (ppv == nullptr) return E_POINTER;
    *ppv = nullptr;
    if (clsid != CLSID_ZfxMenu) return CLASS_E_CLASSNOTAVAILABLE;
    auto* factory = new (std::nothrow) ClassFactory();
    if (factory == nullptr) return E_OUTOFMEMORY;
    HRESULT result = factory->QueryInterface(riid, ppv);
    factory->Release();
    return result;
}

STDAPI DllCanUnloadNow()
{
    return g_objects == 0 ? S_OK : S_FALSE;
}
