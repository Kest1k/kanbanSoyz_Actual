public override bool DoHandleExternalLink( ScriptingObject obj, String url, bool openTree )
{
    var io = obj as InfoObject;
    if( io == null ) return false;

    // 14068UL - тестовый сервер
    // 804663UL - основной сервер
    if( io.Id == 14068UL || io.Id == 804663UL )
    {
        OpenBoardAndRefresh( io );
        return true;
    }

    return false;
}

private void OpenBoardAndRefresh( InfoObject page )
{
    BringPlmToFront();
    try { Service.UI.DoUIEventsDispatching(); } catch { }

    try 
    { 
        bool tabActivated = false;
        var allPanels = Service.UI.GetBrowserPanelsFromAllGroups();
        if (allPanels != null)
        {
            var existingTab = allPanels.FirstOrDefault(p => 
            {
                try
                {
                    var prop = p.GetType().GetProperty("ScriptingObject");
                    if (prop != null)
                    {
                        var o = prop.GetValue(p, null) as ScriptingObject;
                        if (o != null && o.Id == page.Id) return true;
                    }
                }
                catch { }
                return false;
            });

            if (existingTab != null)
            {
                existingTab.Activate();
                tabActivated = true;
            }
        }

        if (!tabActivated)
        {
            Service.UI.OpenPropertiesPane( page );
        }
    } 
    catch { }

    try { Service.UI.DoUIEventsDispatching(); } catch { }

    BringPlmToFront();
    RefreshBoardWithDelay( page );
}

private void RefreshBoardWithDelay( InfoObject page )
{
    try
    {
        var ctrl = Service.UI.SyncControl;
        if( ctrl == null )
        {
            RefreshBoardNow( page );
            return;
        }

        ctrl.BeginInvoke( (Action)( () =>
        {
            // Мы убрали мгновенный RefreshBoardNow( page ); отсюда, 
            // чтобы не было двойного моргания.

            var timer = new System.Windows.Forms.Timer();
            timer.Interval = 300; // 300 мс вполне достаточно для прогрузки UI
            timer.Tick += ( s, e ) =>
            {
                timer.Stop();
                timer.Dispose();
                RefreshBoardNow( page ); // Однократное обновление
            };
            timer.Start();
        } ) );
    }
    catch
    {
        RefreshBoardNow( page );
    }
}

private void RefreshBoardNow( InfoObject page )
{
    // 1. Подтягиваем свежие данные (новые задачи) с сервера
    try { Service.UI.LoadChangesFromServer( true ); } catch { }
    try { Service.UI.DoUIEventsDispatching(); } catch { }
    
    // 2. Ставим флаг, который обычно слушают HTML-экраны в PLM
    try { page.PropertyBag["RefreshRequested"] = true; } catch { }
    
    // 3. Системная команда ядру PLM: "Сбрось кэш отрисовки этого объекта"
    try { Service.UI.RefreshObject( page ); } catch { }
    
    // 4. Ваши кастомные методы (оставляем как было)
    try { page.Invoke( "RefreshBoard", null ); } catch { }
    try { page.Invoke( "Refresh", null ); } catch { }

    // 5. БРОНЕБОЙНЫЙ РЕБИЛД ОТКРЫТОЙ ВКЛАДКИ (UI)
    try
    {
        var allPanels = Service.UI.GetBrowserPanelsFromAllGroups();
        if (allPanels != null)
        {
            var existingTab = allPanels.FirstOrDefault(p => 
            {
                try
                {
                    var prop = p.GetType().GetProperty("ScriptingObject");
                    if (prop != null)
                    {
                        var o = prop.GetValue(p, null) as ScriptingObject;
                        if (o != null && o.Id == page.Id) return true;
                    }
                }
                catch { }
                return false;
            });

            if (existingTab != null)
            {
                // Вытаскиваем IPropertySheetCallback и принудительно перестраиваем интерфейс
                var propSheet = existingTab.TargetPanel as ProgramSoyuz.PLM.Scripting.IPropertySheetCallback;
                if (propSheet != null)
                {
                    propSheet.Rebuild();
                }
            }
        }
    }
    catch { }
}

private void BringPlmToFront()
{
    var ctrl = Service.UI.SyncControl;
    if (ctrl != null && ctrl.InvokeRequired)
    {
        ctrl.BeginInvoke((Action)BringPlmToFrontInternal);
        return;
    }
    BringPlmToFrontInternal();
}

private void BringPlmToFrontInternal()
{
    var pid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

    try
    {
        using (var invisibleForm = new System.Windows.Forms.Form())
        {
            invisibleForm.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            invisibleForm.ShowInTaskbar = false;
            invisibleForm.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            invisibleForm.Location = new System.Drawing.Point(-20000, -20000);
            invisibleForm.Size = new System.Drawing.Size(1, 1);
            invisibleForm.Opacity = 0;
            invisibleForm.Shown += (s, e) => invisibleForm.Close();
            invisibleForm.ShowDialog();
        }
    }
    catch { }

    IntPtr targetHandle = IntPtr.Zero;

    if (Service.UI.MainWindow != null)
        targetHandle = Service.UI.MainWindow.Handle;

    if (targetHandle == IntPtr.Zero)
    {
        try { targetHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; } catch { }
    }

    if (targetHandle == IntPtr.Zero)
    {
        EnumWindows((hwnd, lParam) =>
        {
            GetWindowThreadProcessId(hwnd, out uint wPid);
            if (wPid == pid)
            {
                var sb = new System.Text.StringBuilder(512);
                GetWindowText(hwnd, sb, sb.Capacity);
                string title = sb.ToString();
                if (title.Contains("Союз-PLM") || title.Contains("Sojuz-PLM") || title.Contains("PLM"))
                {
                    targetHandle = hwnd;
                    return false; 
                }
            }
            return true; 
        }, IntPtr.Zero);
    }

    if (targetHandle != IntPtr.Zero)
    {
        try
        {
            var mainForm = System.Windows.Forms.Control.FromHandle(targetHandle) as System.Windows.Forms.Form;
            if (mainForm != null)
            {
                Action restoreNetProps = () => {
                    if (!mainForm.Visible) mainForm.Visible = true;
                    if (!mainForm.ShowInTaskbar) mainForm.ShowInTaskbar = true;
                    if (mainForm.WindowState == System.Windows.Forms.FormWindowState.Minimized)
                        mainForm.WindowState = System.Windows.Forms.FormWindowState.Normal;
                };

                if (mainForm.InvokeRequired) mainForm.Invoke(restoreNetProps);
                else restoreNetProps();
            }
        }
        catch { }

        ShowWindow(targetHandle, 9); // SW_RESTORE
        SetForegroundWindow(targetHandle);
    }
}

[System.Runtime.InteropServices.DllImport("user32.dll")]
private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

[System.Runtime.InteropServices.DllImport("user32.dll")]
private static extern bool SetForegroundWindow(IntPtr hWnd);

[System.Runtime.InteropServices.DllImport("user32.dll")]
private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

[System.Runtime.InteropServices.DllImport("user32.dll")]
[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);