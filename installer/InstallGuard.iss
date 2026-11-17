// 在 InstallDelete 和文件复制之前检查，不能依赖 Restart Manager 关闭管理员进程。
function OpenUpdateFile(FileName: String; Access, Share: Cardinal;
  Security: Integer; Creation, Flags: Cardinal; Template: Integer): THandle;
  external 'CreateFileW@kernel32.dll stdcall';
function CloseUpdateFile(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function CheckUpdateFile(const FileName: String): String;
var
  Handle: THandle;
begin
  Result := '';
  // OPEN_EXISTING：只申请写入/删除权限，不写、不截断、不删除文件。
  // 同时检查 DLL 映射和普通文件句柄，子进程即使在应用目录外也不能漏掉。
  Handle := OpenUpdateFile(FileName, $40010000, 7, 0, 3, 0, 0);
  if Handle = THandle(-1) then
    Result := FileName
  else
    CloseUpdateFile(Handle);
end;

function CheckUpdateFiles(const Directory, Mask: String; Recursive: Boolean): String;
var
  FindRec: TFindRec;
  FileName: String;
begin
  Result := '';
  if FindFirst(AddBackslash(Directory) + Mask, FindRec) then
  begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          FileName := AddBackslash(Directory) + FindRec.Name;
          if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          begin
            if Recursive then Result := CheckUpdateFiles(FileName, '*', True);
          end
          else
            Result := CheckUpdateFile(FileName);
        end;
        if Result <> '' then Break;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function CheckAssistantFiles(const Directory: String): String;
var
  Blocked: String;
  Subdirectories: TArrayOfString;
  I: Integer;
begin
  Result := '';
  if CheckForMutexes('Local\StrongholdAssistant') then
  begin
    Result := '高效雅雅还开着（可能是以管理员身份运行的），请先在托盘或任务管理器里退出它，再点重试。';
    Exit;
  end;
  // 只检查安装器拥有的文件，避免把用户放在同目录的其他程序算进来。
  Blocked := CheckUpdateFiles(Directory, 'StrongholdAssistant.exe', False);
  if Blocked = '' then Blocked := CheckUpdateFiles(Directory, '*.dll', False);
  if Blocked = '' then Blocked := CheckUpdateFiles(Directory, 'StrongholdAssistant.*.json', False);
  Subdirectories := ['runtimes', 'plugins', 'data', 'ui'];
  for I := 0 to GetArrayLength(Subdirectories) - 1 do
  begin
    if Blocked <> '' then Break;
    Blocked := CheckUpdateFiles(AddBackslash(Directory) + Subdirectories[I], '*', True);
  end;
  if Blocked <> '' then
    Result := '高效雅雅的文件还被占用，或当前没有修改权限。请先在托盘或任务管理器里退出助手（可能是以管理员身份运行的）及它的残留进程，再点重试。' + #13#10 + Blocked;
end;
