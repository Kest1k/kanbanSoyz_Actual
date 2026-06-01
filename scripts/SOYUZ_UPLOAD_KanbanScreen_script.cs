// Методы Invoke:
//   BeforeRender      – загружает задачи, фильтрует по роли и режиму
//   MoveTask          (nameKey|newStatus) → меняет KanbanStatus
//   CreateTask        (title|status|priority|dueDate|details) → создаёт KanbanTask
//   OpenTask          (nameKey) → открывает карточку PLM
//   DeleteTask        (nameKey) → удаляет (только создатель)
//   RefreshBoard      → перерендеривает доску
//   GetHierarchyInfo  → JSON {role, myContext, divisions[], sectors[], users[]}
//   SetViewMode       (mode) → сохраняет режим просмотра в PropertyBag
private const int STATUS_DONE = 3;
private const string LEAD_ENGINEER_ROLE = "leadEngineer";
private const string LEAD_ENGINEER_NAMEKEY_PREFIX = "ved";
private static long lastTick = 0;

public override Object Invoke( String methodName, InfoObject obj, Object inputParams )
{
    try
    {
        switch( methodName )
        {
            case "BeforeRender":
            {
                var renderArgs = (inputParams as Object[]).SafeGetItem(0) as IDictionary<String, Object>;

                var container = Service.GetDataContainer( "All_Kanban_Tasks_Folder" );
                if( container == null ) break;

                // 4 колонки: 0=Надо сделать 1=В работе 2=Ожидание 3=Готово
                var cols    = new List<object>[4];
                for( int i = 0; i < 4; i++ )
                    cols[i] = new List<object>();

                // Сначала собираем InfoObject-ы по колонкам, потом сортируем
                var raw = new List<InfoObject>[4];
                for( int i = 0; i < 4; i++ )
                    raw[i] = new List<InfoObject>();

                var currentUser = Service.GetCurrentUser();
                var role        = GetUserRole( currentUser );
                var viewMode    = (obj.PropertyBag["KbViewMode"] as string) ?? "my";
                var allowedIds  = GetAllowedUserIdSet( currentUser, role, viewMode );

                string myCreatorKey = "";
                if( viewMode == "myCreated" && currentUser != null )
                {
                    myCreatorKey = !string.IsNullOrEmpty( currentUser.NameKey ) ? currentUser.NameKey
                                 : !string.IsNullOrEmpty( currentUser.AccountId ) ? currentUser.AccountId
                                 : currentUser.Id.ToString();
                }

                // Фильтр по периоду (из PropertyBag, формат dd.MM.yyyy)
                DateTime? periodFrom = null, periodTo = null;
                {
                    var sFrom = obj.PropertyBag["KbPeriodFrom"] as string;
                    var sTo   = obj.PropertyBag["KbPeriodTo"]   as string;
                    if( !string.IsNullOrEmpty( sFrom ) )
                    {
                        DateTime tmp;
                        if( DateTime.TryParseExact( sFrom, "dd.MM.yyyy",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out tmp ) )
                            periodFrom = tmp;
                    }
                    if( !string.IsNullOrEmpty( sTo ) )
                    {
                        DateTime tmp;
                        if( DateTime.TryParseExact( sTo, "dd.MM.yyyy",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out tmp ) )
                            periodTo = tmp;
                    }
                }

                foreach( var task in container.RootInfoObjects )
                {
                    var assignee = task.GetUser( "Assignee" );
                    if( assignee == null ) continue;

                    // allowedIds == null → режим «all» для admin, показываем всё
                    if( allowedIds != null && !allowedIds.Contains( assignee.Id.ToString() ) )
                        continue;
                    if( !CanUserSeeTask( task, currentUser ) )
                        continue;

                    if( viewMode == "myCreated" )
                    {
                        var taskCreator = "";
                        try { taskCreator = task.GetString( "Creator" ) ?? ""; } catch { }
                        if( taskCreator != myCreatorKey ) continue;
                        if( assignee.Id.ToString() == currentUser.Id.ToString() ) continue;
                    }

                    int status = GetStatusIndex( task );
                    if( status < 0 || status > 3 ) status = 0;

                    // Фильтр по периоду
                    if( !IsTaskInPeriod( task, status, periodFrom, periodTo ) ) continue;

                    raw[status].Add( task );
                }

                // Передаём роль и режим в шаблон (для инициализации JS-иерархии)
                renderArgs["kbRole"]     = role;
                renderArgs["kbViewMode"] = viewMode;

                // Передаём даты периода в Liquid (для восстановления в UI)
                renderArgs["KbPeriodFrom"] = (obj.PropertyBag["KbPeriodFrom"] as string) ?? "";
                renderArgs["KbPeriodTo"]   = (obj.PropertyBag["KbPeriodTo"]   as string) ?? "";

                // Колонки 0-2: сортировка по приоритету (urgent → high → medium → low),
                // внутри одного приоритета – по DateCreated убывающе (новые выше)
                for( int i = 0; i < STATUS_DONE; i++ )
                    raw[i].Sort( CompareActiveTasks );

                // Колонка 3 (Готово): сортируем по CompletedDate убывающе (с учётом времени)
                raw[STATUS_DONE].Sort( (a, b) => {
                    DateTime da = default(DateTime), db = default(DateTime);
                    try { da = a.GetValue<DateTime>( "CompletedDate" ); } catch {}
                    try { db = b.GetValue<DateTime>( "CompletedDate" ); } catch {}
                    return db.CompareTo( da );
                });

                for( int i = 0; i < 4; i++ )
                    foreach( var task in raw[i] )
                        cols[i].Add( BuildCardData( task, i ) );

                renderArgs["col_0"] = cols[0];
                renderArgs["col_1"] = cols[1];
                renderArgs["col_2"] = cols[2];
                renderArgs["col_3"] = cols[3];

                // Список тегов для панели создания задачи
                var tagsList = new List<object>();
                try
                {
                    var rootNv = Service.GetNamedValue( "Ref_KanbanTags" );
                    if( rootNv != null )
                    {
                        foreach( var t in rootNv.AllChildren )
                        {
                            if( t.IsHiddenInUI ) continue;
                            var tn = t.ToString() ?? "";
                            if( !string.IsNullOrEmpty( tn ) )
                                tagsList.Add( new { name = HtmlEnc( tn ) } );
                        }
                    }
                    if( tagsList.Count == 0 )
                    {
                        var tc = Service.GetDataContainer( "Ref_KanbanTags" );
                        if( tc != null )
                            foreach( var t in tc.RootInfoObjects )
                                tagsList.Add( new { name = HtmlEnc( t.ToString() ?? "" ) } );
                    }
                }
                catch { }
                renderArgs["availableTags"] = tagsList;

                var autoOpen = obj.PropertyBag["AutoOpenTask"] as string;
                if( !string.IsNullOrEmpty( autoOpen ) )
                    renderArgs["autoOpenTask"] = autoOpen;

                break;
            }

            case "MoveTask":    return DoMoveTask( inputParams );
            case "CreateTask":       return DoCreateTask( inputParams );
            case "CreateGroupTask":  return DoCreateGroupTask( inputParams );
            case "OpenTask":    return DoOpenTask( inputParams );
            case "DeleteTask":  return DoDeleteTask( inputParams );
            case "RefreshBoard":
            {
                Service.UI.SyncControl.BeginInvoke( (Action)( () => obj.Invoke( "Refresh", null ) ) );
                return "OK";
            }
            case "GetHierarchyInfo": return DoGetHierarchyInfo( obj, inputParams );
            case "SetViewMode":      return DoSetViewMode( obj, inputParams );
            case "SetPeriodFilter":
            {
                // 1) Записываем PropertyBag (sync). 2) Сразу триггерим Refresh
                //    в одном Invoke, чтобы BeforeRender гарантированно увидел
                //    свежие KbPeriodFrom/KbPeriodTo. Раздельные вызовы из JS
                //    давали гонку на некоторых стендах.
                var result = DoSetPeriodFilter( obj, inputParams );
                var s = result as string;
                if( s != null && s.IndexOf( "ERROR" ) == 0 ) return s;
                Service.UI.SyncControl.BeginInvoke( (Action)( () => obj.Invoke( "Refresh", null ) ) );
                return "OK";
            }
            case "GetReport":        return DoGetReport( inputParams );
            case "ExportToExcel":    return DoExportToExcel( obj, inputParams );
            case "GetTaskDetails":   return DoGetTaskDetails( inputParams );
            case "SaveTask":         return DoSaveTask( inputParams );
            case "GetTaskRevisions":   return DoGetTaskHistory( inputParams );
            case "GetTaskHistory":     return DoGetTaskHistory( inputParams );
            case "AddAttachment":      return DoAddAttachment( inputParams );
            case "RemoveAttachment":   return DoRemoveAttachment( inputParams );
            case "GetAttachments":     return DoGetAttachments( inputParams );
            case "SearchObjects":      return DoSearchObjects( inputParams );
            case "OpenObject":         return DoOpenObject( inputParams );
            case "PickObject":              return DoPickObject( inputParams );
            case "PickObjects":             return DoPickObjects( inputParams );
            case "PickAndAttach":           return DoPickAndAttach( inputParams );
            case "PickContainers":          return DoPickContainers( inputParams );
            case "PickAndAttachContainer":  return DoPickAndAttachContainer( inputParams );
            case "AddContainer":            return DoAddContainer( inputParams );
            case "RemoveContainer":         return DoRemoveContainer( inputParams );
            case "OpenContainer":           return DoOpenContainer( inputParams );
            case "PickLocalFiles":          return DoPickLocalFiles( inputParams );
            case "PickStorageContainer":    return DoPickStorageContainer( inputParams );
            case "AttachLocalFile":         return DoAttachLocalFile( inputParams );
            case "EditLocalFile":           return DoEditLocalFile( inputParams );
            case "SaveLocalFileEdit":       return DoSaveLocalFileEdit( inputParams );
            case "CancelLocalFileEdit":     return DoCancelLocalFileEdit( inputParams );
            case "OpenDirectumLink":        return DoOpenDirectumLink( inputParams );
            case "AddComment":    return DoAddComment( inputParams );
            case "GetComments":   return DoGetComments( inputParams );
            case "DeleteComment": return DoDeleteComment( inputParams );
            case "EditComment":   return DoEditComment( inputParams );
            case "GetCardCommentCount": return DoGetCardCommentCount( inputParams );
            case "AddSubtask":    return DoAddSubtask( inputParams );
            case "ToggleSubtask": return DoToggleSubtask( inputParams );
            case "DeleteSubtask": return DoDeleteSubtask( inputParams );
            case "GetSubtasks":   return DoGetSubtasks( inputParams );
            case "EditSubtask":     return DoEditSubtask( inputParams );
            case "ReorderSubtasks": return DoReorderSubtasks( inputParams );
            case "ClearAutoOpen": obj.PropertyBag.Remove( "AutoOpenTask" ); return "OK";
            case "GetWhatsNewSeen": return DoGetWhatsNewSeen();
            case "SetWhatsNewSeen": return DoSetWhatsNewSeen( inputParams );
        }
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "KanbanScreen.Invoke [" + methodName + "]: " + ex.Message );
        return "ERROR:" + ex.Message;
    }
    return null;
}

// MoveTask
// inputParams (JS → C#): object[] { "nameKey|newStatus|returnReason|newDueDate" }

private object DoMoveTask( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 4 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var nameKey   = parts[0].Trim();
    var comment   = parts.Length > 2 ? parts[2].Trim() : "";
    var newDueRaw = parts.Length > 3 ? parts[3].Trim() : "";
    int newStatus;
    if( !int.TryParse( parts[1].Trim(), out newStatus ) || newStatus < 0 || newStatus > 3 )
        return "ERROR:BadStatus";

    var task = GetTaskByKeyOrNull( nameKey );
    if( task == null ) return "ERROR:TaskNotFound:" + nameKey;

    // Проверка прав на перемещение
    // Двигать может: исполнитель, создатель или админ
    var currentUser = Service.GetCurrentUser();
    var curRole     = GetUserRole( currentUser );
    if( !CanUserSeeTask( task, currentUser ) ) return "ERROR:Forbidden";
    if( curRole != "admin" )
    {
        bool isOwner = false;
        try
        {
            var assignee = task.GetUser( "Assignee" );
            if( assignee != null && currentUser != null && assignee.Id == currentUser.Id )
                isOwner = true;
        }
        catch { }
        if( !isOwner )
        {
            try
            {
                var creatorKey = task.GetString( "Creator" ) ?? "";
                var curKey = !string.IsNullOrEmpty( currentUser.NameKey ) ? currentUser.NameKey
                           : !string.IsNullOrEmpty( currentUser.AccountId ) ? currentUser.AccountId
                           : currentUser.Id.ToString();
                if( creatorKey == curKey ) isOwner = true;
            }
            catch { }
        }
        if( !isOwner ) return "ERROR:NotOwner";
    }

    int oldStatus = GetStatusIndex( task );
    bool isReturnFromDone = (oldStatus == STATUS_DONE && newStatus != STATUS_DONE);
    string oldDueDate = "";
    try
    {
        var oldDue = task.GetValue<DateTime>( "DueDate" );
        if( oldDue != default(DateTime) ) oldDueDate = oldDue.ToString( "dd.MM.yyyy" );
    }
    catch { }

    if( isReturnFromDone )
    {
        string creatorKey = "";
        try { creatorKey = task.GetString( "Creator" ) ?? ""; } catch { }
        var curKey = GetUserStableKey( currentUser );
        if( curRole != "admin" && !string.IsNullOrEmpty( creatorKey ) && creatorKey != curKey )
            return "ERROR:OnlyCreatorCanReturn";

        if( string.IsNullOrEmpty( comment ) ) return "ERROR:ReturnReasonRequired";
        if( comment.Length > 2000 ) return "ERROR:ReturnReasonTooLong";

        DateTime newDue;
        if( !DateTime.TryParseExact( newDueRaw, "dd.MM.yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out newDue ) )
            return "ERROR:NewDueDateRequired";

        DateTime origDue = default(DateTime);
        try { origDue = task.GetValue<DateTime>( "OriginalDueDate" ); } catch { }
        if( origDue == default(DateTime) )
        {
            DateTime currentDue = default(DateTime);
            try { currentDue = task.GetValue<DateTime>( "DueDate" ); } catch { }
            if( currentDue != default(DateTime) )
                try { task["OriginalDueDate"] = currentDue; } catch { }
        }

        try { task["DueDate"] = newDue; } catch { }
        int rc = 0;
        try { rc = task.GetValue<int>( "ReturnCount" ); } catch { }
        try { task["ReturnCount"] = rc + 1; } catch { }

        System.Collections.Generic.Dictionary<string, string> ignored;
        var addCommentErr = AppendCommentToTask( task, currentUser,
            "Возврат из «Готово». Новый срок: " + newDue.ToString( "dd.MM.yyyy" ) + ".\n\n" + comment,
            false, out ignored );
        if( !string.IsNullOrEmpty( addCommentErr ) ) return addCommentErr;
    }

    // Устанавливаем новый статус через NamedValue (KanbanStatus – Enumeration)
    SetTaskStatus( task, newStatus );

    // Логика CompletedDate
    if( newStatus == STATUS_DONE && oldStatus != STATUS_DONE )
    {
        // Переход В «Готово» – записываем дату завершения
        task["CompletedDate"] = DateTime.Now;
    }
    else if( isReturnFromDone )
    {
        // Возврат ИЗ «Готово» – очищаем дату
        task["CompletedDate"] = null;
    }

    // ChangeLog: записываем смену колонки
    if( oldStatus != newStatus )
    {
        try
        {
            string[] stNames = new string[]{ "Надо сделать", "В работе", "Ожидание", "Готово" };
            var osn = oldStatus >= 0 && oldStatus < 4 ? stNames[oldStatus] : "?";
            var nsn = newStatus >= 0 && newStatus < 4 ? stNames[newStatus] : "?";
            string authorName = "";
            try { var cu = Service.GetCurrentUser(); if( cu != null ) authorName = cu.ToString() ?? ""; } catch { }
            var changes = "[{\"f\":\"Статус\",\"o\":\"" + JsonEscape( osn ) + "\",\"n\":\"" + JsonEscape( nsn ) + "\"}";
            if( isReturnFromDone )
                changes += ",{\"f\":\"Срок\",\"o\":\"" + JsonEscape( oldDueDate ) + "\",\"n\":\"" + JsonEscape( newDueRaw ) + "\"}";
            changes += "]";
            var entry = "{\"d\":\"" + DateTime.Now.ToString( "dd.MM.yyyy HH:mm" )
                      + "\",\"a\":\"" + JsonEscape( authorName )
                      + "\",\"c\":" + changes + "}";
            var oldLog = task.GetString( "ChangeLog" ) ?? "";
            string newLog;
            if( string.IsNullOrEmpty( oldLog ) || oldLog.Trim() == "[]" )
                newLog = "[" + entry + "]";
            else
                newLog = oldLog.TrimEnd().TrimEnd( ']' ) + "," + entry + "]";
            task["ChangeLog"] = newLog;
        }
        catch { /* ChangeLog не критичен */ }
    }

    // Единственный Save
    task.Save();
    return "OK";
}


// CreateTask
// inputParams (JS → C#): object[] { "title|status|priority|dueDate|details|tags|assigneeKey|isPrivate" }
// Только title обязателен; остальные – с дефолтами.
private object DoCreateTask( object inputParams )
{
    var raw = GetParamStr( inputParams );

    // Разбираем: title|status|priority|dueDate|details|tags|assigneeKey|isPrivate
    var parts  = ParsePipeArgs( raw, 8 );
    var title  = parts.Length > 0 ? parts[0].Trim() : "";
    if( string.IsNullOrEmpty( title ) ) return "ERROR:EmptyTitle";

    int status = 0;
    if( parts.Length > 1 && !string.IsNullOrEmpty( parts[1].Trim() ) )
        int.TryParse( parts[1].Trim(), out status );
    if( status < 0 || status > 3 ) status = 0;

    var priority = "medium";
    if( parts.Length > 2 && !string.IsNullOrEmpty( parts[2].Trim() ) )
        priority = parts[2].Trim();

    var dueDateStr  = parts.Length > 3 ? parts[3].Trim() : "";
    var detailsStr  = parts.Length > 4 ? parts[4].Trim() : "";
    var tagsStr     = parts.Length > 5 ? parts[5].Trim() : "";
    var assigneeKey = parts.Length > 6 ? parts[6].Trim() : "";
    var isPrivate   = parts.Length > 7 && parts[7].Trim() == "1";

    // Создаём объект
    var container = Service.GetDataContainer( "All_Kanban_Tasks_Folder" );
    if( container == null ) return "ERROR:ContainerNotFound";

    var template = Service.GetTemplate( "KanbanTask" );
    if( template == null ) return "ERROR:TemplateNotFound";

    var task    = new InfoObject( container, template );
    var nameKey = "ktask_" + System.DateTime.Now.Ticks.ToString();
    task.NameKey = nameKey;

    task["TaskName"] = title;
    SetTaskStatus( task, status );

    // Приоритет
    var priorityPath = priority == "urgent" ? @"Ref_KanbanPriority\Urgent"
                     : priority == "high"   ? @"Ref_KanbanPriority\High"
                     : priority == "low"    ? @"Ref_KanbanPriority\Low"
                     :                        @"Ref_KanbanPriority\Medium";
    var priorityNv = Service.GetNamedValue( priorityPath );
    if( priorityNv != null ) task["Priority"] = priorityNv;

    // Срок выполнения
    if( !string.IsNullOrEmpty( dueDateStr ) )
    {
        DateTime dueDate;
        if( DateTime.TryParseExact( dueDateStr, "dd.MM.yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None, out dueDate ) )
            task["DueDate"] = dueDate;
    }

    // Описание
    if( !string.IsNullOrEmpty( detailsStr ) )
        task["TaskDetails"] = detailsStr;

    // Теги
    if( !string.IsNullOrEmpty( tagsStr ) )
        task["Tags"] = tagsStr;

    try { task["IsPrivate"] = isPrivate; } catch { }

    // Исполнитель = текущий пользователь (по умолчанию)
    var currentUser = Service.GetCurrentUser();
    var currentRole = GetUserRole( currentUser );
    if( currentUser != null )
    {
        task["Assignee"] = currentUser;
        // Создатель – всегда тот, кто создаёт задачу (для проверки прав удаления)
        try
        {
            var ck = string.IsNullOrEmpty( currentUser.NameKey ) ? currentUser.AccountId : currentUser.NameKey;
            if( !string.IsNullOrEmpty( ck ) ) task["Creator"] = ck;
        }
        catch { }
    }

    // Переопределить исполнителя если выбран конкретный сотрудник
    if( !string.IsNullOrEmpty( assigneeKey ) )
    {
        var assigneeUser = FindUserByKeyOrNull( assigneeKey );
        if( assigneeUser == null ) return "ERROR:AssigneeNotFound";
        if( !CanAssignUserInScope( currentUser, currentRole, assigneeUser ) )
            return "ERROR:AssigneeNotAllowed";
        task["Assignee"] = assigneeUser;
    }

    task.Save();
    return nameKey;
}

// CreateGroupTask
// inputParams: "title|status|priority|dueDate|details|tags|key1,key2,key3"
// Создаёт по одной задаче для каждого исполнителя из списка.
private object DoCreateGroupTask( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 7 );

    var title = parts.Length > 0 ? parts[0].Trim() : "";
    if( string.IsNullOrEmpty( title ) ) return "ERROR:EmptyTitle";

    int status = 0;
    if( parts.Length > 1 && !string.IsNullOrEmpty( parts[1].Trim() ) )
        int.TryParse( parts[1].Trim(), out status );
    if( status < 0 || status > 3 ) status = 0;

    var priority   = parts.Length > 2 && !string.IsNullOrEmpty( parts[2].Trim() ) ? parts[2].Trim() : "medium";
    var dueDateStr = parts.Length > 3 ? parts[3].Trim() : "";
    var detailsStr = parts.Length > 4 ? parts[4].Trim() : "";
    var tagsStr    = parts.Length > 5 ? parts[5].Trim() : "";
    var keysStr    = parts.Length > 6 ? parts[6].Trim() : "";

    if( string.IsNullOrEmpty( keysStr ) ) return "ERROR:NoAssignees";
    var assigneeKeys = keysStr.Split( new char[]{ ',' }, StringSplitOptions.RemoveEmptyEntries );
    if( assigneeKeys.Length == 0 ) return "ERROR:NoAssignees";

    // Строим словарь ключ → User для быстрого поиска
    var userMap = new System.Collections.Generic.Dictionary<string, User>();
    foreach( var u in Service.AllUsers )
    {
        if( u.IsGroup ) continue;
        var k = !string.IsNullOrEmpty( u.NameKey ) ? u.NameKey
              : !string.IsNullOrEmpty( u.AccountId ) ? u.AccountId
              : u.Id.ToString();
        if( !userMap.ContainsKey( k ) ) userMap[k] = u;
    }

    var container = Service.GetDataContainer( "All_Kanban_Tasks_Folder" );
    if( container == null ) return "ERROR:ContainerNotFound";
    var template = Service.GetTemplate( "KanbanTask" );
    if( template == null ) return "ERROR:TemplateNotFound";

    var currentUser = Service.GetCurrentUser();
    var currentRole = GetUserRole( currentUser );
    var creatorKey  = currentUser == null ? "" :
                      string.IsNullOrEmpty( currentUser.NameKey ) ? currentUser.AccountId : currentUser.NameKey;

    var priorityPath = priority == "urgent" ? @"Ref_KanbanPriority\Urgent"
                     : priority == "high"   ? @"Ref_KanbanPriority\High"
                     : priority == "low"    ? @"Ref_KanbanPriority\Low"
                     :                        @"Ref_KanbanPriority\Medium";
    var priorityNv = Service.GetNamedValue( priorityPath );

    DateTime dueDate = default(DateTime);
    if( !string.IsNullOrEmpty( dueDateStr ) )
        DateTime.TryParseExact( dueDateStr, "dd.MM.yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out dueDate );

    int count = 0;
    foreach( var assigneeKey in assigneeKeys )
    {
        var key = assigneeKey.Trim();
        if( string.IsNullOrEmpty( key ) || !userMap.ContainsKey( key ) ) continue;
        var assignee = userMap[key];
        if( !CanAssignUserInScope( currentUser, currentRole, assignee ) ) continue;

        var task = new InfoObject( container, template );
        // Уникальный NameKey: метка + счётчик
        task.NameKey = "ktask_" + System.DateTime.Now.Ticks.ToString() + "_g" + count;
        task["TaskName"] = title;
        SetTaskStatus( task, status );
        if( priorityNv != null ) task["Priority"] = priorityNv;
        if( dueDate != default(DateTime) ) task["DueDate"] = dueDate;
        if( !string.IsNullOrEmpty( detailsStr ) ) task["TaskDetails"] = detailsStr;
        if( !string.IsNullOrEmpty( tagsStr ) ) task["Tags"] = tagsStr;
        task["Assignee"] = assignee;
        try { task["IsPrivate"] = false; } catch { }
        if( !string.IsNullOrEmpty( creatorKey ) )
            try { task["Creator"] = creatorKey; } catch { }
        task.Save();
        count++;
    }

    if( count == 0 ) return "ERROR:NoAllowedAssignees";
    return "OK:" + count;
}

// OpenTask
private object DoOpenTask( object inputParams )
{
    var nameKey = GetParamStr( inputParams );
    if( string.IsNullOrEmpty( nameKey ) ) return "ERROR:EmptyId";

    var task = GetTaskByKeyOrNull( nameKey );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    Service.UI.OpenPropertiesPane( task );
    return "OK";
}

// DeleteTask
private object DoDeleteTask( object inputParams )
{
    var nameKey = GetParamStr( inputParams );
    if( string.IsNullOrEmpty( nameKey ) ) return "ERROR:EmptyId";

    var task = GetTaskByKeyOrNull( nameKey );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    // Создатель или начальник подчинённого может удалить задачу
    try
    {
        var creatorKey  = task.GetString( "Creator" ) ?? "";
        var currentUser = Service.GetCurrentUser();
        if( !string.IsNullOrEmpty( creatorKey ) )
        {
            if( currentUser == null ) return "ERROR:NotOwner";
            var curKey = string.IsNullOrEmpty( currentUser.NameKey ) ? currentUser.AccountId : currentUser.NameKey;
            if( creatorKey != curKey )
            {
                var delRole = GetUserRole( currentUser );
                if( delRole == "admin" )
                { /* разрешаем */ }
                else if( delRole == "headOfDept" || HasSectorScopeRole( delRole ) )
                {
                    var creatorUser = FindUserByKeyOrNull( creatorKey );
                    if( creatorUser == null || !CanAssignUserInScope( currentUser, delRole, creatorUser ) )
                        return "ERROR:NotOwner";
                }
                else
                {
                    return "ERROR:NotOwner";
                }
            }
        }
        // Legacy-задачи без Creator – разрешаем удаление
    }
    catch { /* атрибут Creator не существует – разрешаем удаление */ }

    task.Erase();
    task.Save();
    return "OK";
}
// OnBeforeDisplayInUI - для удобного открытия доски по ссылке
public override void OnBeforeDisplayInUI( InfoObject obj, IPropertySheetCallback propertySheet)
{
         if (propertySheet.IsDialog || propertySheet.ParentBrowserPanel is IPropertiesBrowserPanel) 
            {
             propertySheet.ToolStripVisibility = false;
             propertySheet.TabStripVisibility = false;
            }         
}
// DOM-помощники
public void DropCardById( InfoObject obj, String cardHtmlId )
{
    var web = obj.PropertyBag["Viewer"] as WebBrowser;
    if( web == null || web.IsDisposed ) return;

    var dom  = web.Document.DomDocument as dynamic;
    var node = dom.getElementById( cardHtmlId ) as dynamic;
    if( node != null )
        node.parentNode.removeChild( node );
}

public void MoveCardToColumn( InfoObject obj, String nameKey, int newStatus )
{
    var web = obj.PropertyBag["Viewer"] as WebBrowser;
    if( web == null || web.IsDisposed ) return;

    var doc    = web.Document;
    var card   = doc.GetElementById( "kbc_" + nameKey );
    var target = doc.GetElementById( "kb-body-" + newStatus.ToString() );
    if( card == null || target == null ) return;

    var domCard   = (card.DomElement   as dynamic);
    var domTarget = (target.DomElement as dynamic);
    if( domCard != null && domTarget != null )
        domTarget.appendChild( domCard );
}
// Вспомогательные методы
// КЛЮЧЕВОЙ FIX: window.external.InvokeTemplate передаёт параметры как
// object[] { фактический_параметр }, а не как голую строку.
private string GetParamStr( object inputParams )
{
    var arr = inputParams as object[];
    if( arr != null )
        return arr.Length > 0 ? (arr[0] ?? "").ToString() : "";
    return (inputParams ?? "").ToString();
}

private string[] ParsePipeArgs( string inputParams, int expectedCount )
{
    if( expectedCount < 2 ) expectedCount = 2;
    return (inputParams ?? "").Split( new char[]{ '|' }, expectedCount );
}

private InfoObject GetTaskByKeyOrNull( string nameKey )
{
    return FindTaskByNameKey( nameKey );
}
private InfoObject FindTaskByNameKey( string nameKey )
{
    if( string.IsNullOrEmpty( nameKey ) ) return null;
    var container = Service.GetDataContainer( "All_Kanban_Tasks_Folder" );
    if( container == null ) return null;

    // Задачи без NameKey (созданные вне доски) идентифицируются по числовому Id
    // Формат: "__id_123456"
    if( nameKey.StartsWith( "__id_" ) )
    {
        var idStr = nameKey.Substring( 5 );
        foreach( var t in container.RootInfoObjects )
            if( t.Id.ToString() == idStr ) return t;
        return null;
    }

    foreach( var t in container.RootInfoObjects )
        if( t.NameKey == nameKey ) return t;
    return null;
}

private string GetInitials( string fullName )
{
    if( string.IsNullOrEmpty( fullName ) ) return "";
    var words  = fullName.Split( new char[]{ ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries );
    var result = new System.Text.StringBuilder();
    for( int i = 0; i < words.Length && result.Length < 2; i++ )
        if( words[i].Length > 0 )
            result.Append( char.ToUpper( words[i][0] ) );
    return result.Length > 0 ? result.ToString() : "";
}

private string HtmlEnc( string s )
{
    if( string.IsNullOrEmpty( s ) ) return "";
    return s.Replace( "&", "&amp;" )
            .Replace( "<", "&lt;"  )
            .Replace( ">", "&gt;"  )
            .Replace( "\"", "&quot;" );
}

// Хелперы для KanbanStatus (NamedValue)
// KanbanStatus хранится как Enumeration → Ref_KanbanStatus
// NameKey: Todo=0, InProgress=1, Waiting=2, Done=3
// Value:   "0",    "1",          "2",       "3"

private static readonly string[] STATUS_NAMEKEYS =
    { "Todo", "InProgress", "Waiting", "Done" };

// Ранг приоритета для сортировки (меньше = выше в колонке)
private int PriorityRank( string prio )
{
    if( prio == "urgent" ) return 0;
    if( prio == "high"   ) return 1;
    if( prio == "medium" ) return 2;
    if( prio == "low"    ) return 3;
    return 2; // unknown → как medium
}

// Компаратор для колонок «Надо сделать», «В работе», «Ожидание»:
//   1) Приоритет: urgent → high → medium → low
//   2) Дата создания убывающе (новые сверху) – системное свойство DateCreated
//   3) Id убывающе (страховочный tie-breaker при совпадении даты до миллисекунды)
private int CompareActiveTasks( InfoObject a, InfoObject b )
{
    var aPrio = a.GetNamedValue("Priority")?.GetValue<string>() ?? "medium";
    var bPrio = b.GetNamedValue("Priority")?.GetValue<string>() ?? "medium";
    int aRank = PriorityRank( aPrio );
    int bRank = PriorityRank( bPrio );

    if( aRank != bRank )
        return aRank.CompareTo( bRank );

    // Дата создания – системное свойство Союз-PLM, всегда заполнено
    int dateCmp = b.DateCreated.CompareTo( a.DateCreated );
    if( dateCmp != 0 )
        return dateCmp;

    // Страховочный tie-breaker, если объекты созданы в одну миллисекунду
    return b.Id.CompareTo( a.Id );
}

// Читает статус из задачи → возвращает индекс колонки (0-3)
private int GetStatusIndex( InfoObject task )
{
    try
    {
        var nv = task.GetNamedValue( "KanbanStatus" );
        if( nv != null )
        {
            int idx;
            if( int.TryParse( nv.GetValue<string>(), out idx ) && idx >= 0 && idx <= 3 )
                return idx;
        }
    }
    catch { }
    return 0; // По умолчанию «Надо сделать»
}

// Проверка попадания задачи в период (для BeforeRender)
private bool IsTaskInPeriod( InfoObject task, int statusIdx,
                             DateTime? from, DateTime? to )
{
    if( !from.HasValue && !to.HasValue ) return true;

    DateTime? d = null;
    if( statusIdx == STATUS_DONE )
    {
        try
        {
            var cd = task.GetValue<DateTime>( "CompletedDate" );
            if( cd != DateTime.MinValue ) d = cd;
        }
        catch { }
    }
    if( !d.HasValue )
    {
        d = task.DateCreated;
    }
    if( !d.HasValue ) return true;

    var dt = d.Value.Date;
    if( from.HasValue && dt < from.Value.Date ) return false;
    if( to.HasValue && dt > to.Value.Date ) return false;
    return true;
}

// SetPeriodFilter
private object DoSetPeriodFilter( InfoObject screenObj, object inputParams )
{
    var raw = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    string sFrom = (parts.Length > 0 ? parts[0] : "").Trim();
    string sTo   = (parts.Length > 1 ? parts[1] : "").Trim();

    try
    {
        if( string.IsNullOrEmpty( sFrom ) && string.IsNullOrEmpty( sTo ) )
        {
            screenObj.PropertyBag.Remove( "KbPeriodFrom" );
            screenObj.PropertyBag.Remove( "KbPeriodTo" );
            return "OK";
        }
        if( !string.IsNullOrEmpty( sFrom ) )
        {
            DateTime dFrom;
            if( !DateTime.TryParseExact( sFrom, "dd.MM.yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out dFrom ) )
                return "ERROR:Format";
            screenObj.PropertyBag["KbPeriodFrom"] = sFrom;
        }
        else screenObj.PropertyBag.Remove( "KbPeriodFrom" );

        if( !string.IsNullOrEmpty( sTo ) )
        {
            DateTime dTo;
            if( !DateTime.TryParseExact( sTo, "dd.MM.yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out dTo ) )
                return "ERROR:Format";
            screenObj.PropertyBag["KbPeriodTo"] = sTo;
        }
        else screenObj.PropertyBag.Remove( "KbPeriodTo" );
        return "OK";
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "KanbanScreen.DoSetPeriodFilter: " + ex.Message );
        return "ERROR:Internal";
    }
}

// Записывает статус в задачу по индексу колонки (0-3)
private void SetTaskStatus( InfoObject task, int columnIndex )
{
    if( columnIndex < 0 || columnIndex > 3 ) columnIndex = 0;
    var nv = Service.GetNamedValue( @"Ref_KanbanStatus\" + STATUS_NAMEKEYS[columnIndex] );
    if( nv != null )
        task["KanbanStatus"] = nv;
}

// Строит анонимный объект данных карточки для Liquid-шаблона
private object BuildCardData( InfoObject task, int status )
{
    string assigneeName = "";
    try
    {
        var assigneeUser = task.GetUser( "Assignee" );
        if( assigneeUser != null )
            assigneeName = assigneeUser.ToString() ?? "";
    }
    catch { }

    string dueDateStr = "";
    try
    {
        var dd = task.GetValue<DateTime>( "DueDate" );
        if( dd != default(DateTime) )
            dueDateStr = dd.ToString( "dd.MM.yyyy" );
    }
    catch { }

    string completedDateStr = "";
    try
    {
        var cd = task.GetValue<DateTime>( "CompletedDate" );
        if( cd != default(DateTime) )
            completedDateStr = cd.ToString( "dd.MM.yyyy" );
    }
    catch { }

    string originalDueDateStr = "";
    try
    {
        var odd = task.GetValue<DateTime>( "OriginalDueDate" );
        if( odd != default(DateTime) )
            originalDueDateStr = odd.ToString( "dd.MM.yyyy" );
    }
    catch { }

    int returnCount = 0;
    try { returnCount = task.GetValue<int>( "ReturnCount" ); } catch { }

    var isPrivate = IsTaskPrivate( task ) ? "1" : "0";

    var priority = task.GetNamedValue( "Priority" )?.GetValue<string>() ?? "medium";
    var isUrgent = priority == "urgent" ? "1" : "0";

    // Если NameKey пустой (задача создана вне доски), используем числовой Id как запасной
    var taskId = string.IsNullOrEmpty( task.NameKey )
        ? ("__id_" + task.Id.ToString())
        : task.NameKey;

    // Проверяем право на удаление: только создатель может удалить задачу
    // Инициалы скрываем если текущий пользователь является исполнителем (isSelf)
    var isOwner  = "0";
    var initials = GetInitials( assigneeName );
    string assigneeShort = "";
    User curUser = null;
    string creatorKey = "";
    string curKey = "";
    try
    {
        curUser    = Service.GetCurrentUser();
        creatorKey = task.GetString( "Creator" ) ?? "";
        if( curUser != null )
        {
            curKey = string.IsNullOrEmpty( curUser.NameKey ) ? curUser.AccountId : curUser.NameKey;
            if( !string.IsNullOrEmpty( creatorKey ) && creatorKey == curKey )
                isOwner = "1";
            else if( string.IsNullOrEmpty( creatorKey ) )
                isOwner = "1"; // Legacy-задачи без Creator – разрешаем
            else
            {
                // Начальники могут удалять задачи подчинённых
                var ownerRole = GetUserRole( curUser );
                if( ownerRole == "admin" )
                {
                    isOwner = "1";
                }
                else if( ownerRole == "headOfDept" || HasSectorScopeRole( ownerRole ) )
                {
                    var creatorUser = FindUserByKeyOrNull( creatorKey );
                    if( creatorUser != null && CanAssignUserInScope( curUser, ownerRole, creatorUser ) )
                        isOwner = "1";
                }
            }
        }

        // Если исполнитель = текущий пользователь – инициалы не нужны
        var assigneeUser = task.GetUser( "Assignee" );
        if( assigneeUser != null && curUser != null )
        {
            var aKey = string.IsNullOrEmpty( assigneeUser.NameKey ) ? assigneeUser.AccountId : assigneeUser.NameKey;
            var cKey = string.IsNullOrEmpty( curUser.NameKey )      ? curUser.AccountId      : curUser.NameKey;
            if( aKey == cKey ) initials = "";
        }

        // Фамилия И.О. исполнителя (для отображения на чужих карточках вместо кружка)
        if( assigneeUser != null && !string.IsNullOrEmpty( initials ) )
        {
            try
            {
                var aSurname    = ( assigneeUser.GetString( "GivenName"  ) ?? "" ).Trim();
                var aFirstName  = ( assigneeUser.GetString( "FirstName"  ) ?? "" ).Trim();
                var aPatronymic = ( assigneeUser.GetString( "SecondName" ) ?? "" ).Trim();
                var sbA = new System.Text.StringBuilder();
                if( aSurname.Length > 0 ) sbA.Append( aSurname );
                if( aFirstName.Length > 0 )
                {
                    if( sbA.Length > 0 ) sbA.Append( " " );
                    sbA.Append( char.ToUpper( aFirstName[0] ) ).Append( '.' );
                }
                if( aPatronymic.Length > 0 )
                    sbA.Append( char.ToUpper( aPatronymic[0] ) ).Append( '.' );
                if( sbA.Length > 0 ) assigneeShort = sbA.ToString();
            }
            catch { }
        }
    }
    catch { }

    // Имя создателя (для «Поручил: Фамилия И.О.» на карточке)
    // Не показываем если создатель совпадает с исполнителем
    string creatorShort = "";
    try
    {
        // Определяем ключ исполнителя для сравнения
        string assigneeKey = "";
        try
        {
            var aUser = task.GetUser( "Assignee" );
            if( aUser != null )
                assigneeKey = string.IsNullOrEmpty( aUser.NameKey ) ? aUser.AccountId : aUser.NameKey;
        }
        catch { }

        if( !string.IsNullOrEmpty( creatorKey ) && creatorKey != assigneeKey )
        {
            User creatorUser = null;
            foreach( var u in Service.AllUsers )
            {
                if( u.IsGroup ) continue;
                var uKey = string.IsNullOrEmpty( u.NameKey ) ? u.AccountId : u.NameKey;
                if( uKey == creatorKey ) { creatorUser = u; break; }
            }
            if( creatorUser != null )
            {
                var cSurname    = ( creatorUser.GetString( "GivenName"  ) ?? "" ).Trim();
                var cFirstName  = ( creatorUser.GetString( "FirstName"  ) ?? "" ).Trim();
                var cPatronymic = ( creatorUser.GetString( "SecondName" ) ?? "" ).Trim();
                var sb2 = new System.Text.StringBuilder();
                if( cSurname.Length > 0 ) sb2.Append( cSurname );
                if( cFirstName.Length > 0 )
                {
                    if( sb2.Length > 0 ) sb2.Append( " " );
                    sb2.Append( char.ToUpper( cFirstName[0] ) ).Append( '.' );
                }
                if( cPatronymic.Length > 0 )
                    sb2.Append( char.ToUpper( cPatronymic[0] ) ).Append( '.' );
                creatorShort = sb2.ToString();
            }
        }
    }
    catch { }

    string tags = "";
    try { tags = task.GetString( "Tags" ) ?? ""; } catch { }

    // Просроченность: DueDate < сегодня И задача не завершена (status != 3)
    var isOverdue = "0";
    try
    {
        var dd = task.GetValue<DateTime>( "DueDate" );
        if( dd != default(DateTime) && dd.Date < DateTime.Today && status != 3 )
            isOverdue = "1";
    }
    catch { }

    // Бейдж «НОВАЯ»: задача не в «Готово»/«Ожидание», создана другим, текущий не в SeenByList
    var isNew = "0";
    if( status < 2 )
    {
        try
        {
            if( !string.IsNullOrEmpty( curKey ) && creatorKey != curKey )
            {
                var seenBy = task.GetString( "SeenByList" ) ?? "";
                if( seenBy.IndexOf( curKey ) < 0 )
                    isNew = "1";
            }
        }
        catch { }
    }

    // Денормализованные счётчики подзадач – читаем напрямую, без парсинга JSON.
    int subtaskTotal = 0;
    int subtaskDone  = 0;
    try { subtaskTotal = task.GetValue<int>( "SubtasksTotal" ); } catch { }
    try { subtaskDone  = task.GetValue<int>( "SubtasksDone"  ); } catch { }

    string directumLink = "";
    try { directumLink = task.GetString( "DirectumLink" ) ?? ""; } catch { }
    string hasDirectumLink = string.IsNullOrEmpty( directumLink.Trim() ) ? "0" : "1";

    return new {
        id              = taskId,
        isOwner         = isOwner,
        title           = HtmlEnc( task.GetString( "TaskName" ) ?? "" ),
        details         = HtmlEnc( task.GetString( "TaskDetails" ) ?? "" ),
        status          = status,
        priority        = priority,
        isUrgent        = isUrgent,
        assigneeName    = HtmlEnc( assigneeName ),
        initials        = initials,
        assigneeShort   = HtmlEnc( assigneeShort ),
        dueDate         = dueDateStr,
        completedDate   = completedDateStr,
        originalDueDate = originalDueDateStr,
        returnCount     = returnCount,
        isPrivate       = isPrivate,
        attachmentCount = GetAttachmentCount( task ),
        hasDirectumLink = hasDirectumLink,
        commentCount    = GetCommentCount( task ),
        subtaskTotal    = subtaskTotal,
        subtaskDone     = subtaskDone,
        tags            = HtmlEnc( tags ),
        isOverdue       = isOverdue,
        isNew           = isNew,
        creatorShort    = HtmlEnc( creatorShort ),
        isSelfCreated   = (!string.IsNullOrEmpty( curKey ) && creatorKey == curKey) || string.IsNullOrEmpty( creatorKey ) ? "1" : "0"
    };
}
// Ролевая иерархия (шаг 05)
// Роли: NameKey должностей (Commission)
private static readonly string[] ADMIN_POS = {
    "genKonstr",             // Генеральный конструктор
    "pervZamGenKonstr",      // Первый заместитель ГК
    "pervZamGenKonstrKKIS",  // Первый заместитель ГК (ККИС)
    "rukProject",            // Руководитель проекта
    "rukDirect",             // Руководитель дирекции
    "zamGenKonstrNauch",     // Заместитель ГК (наук.)
    "zamGlavKonstLiana",     // Заместитель главного конструктора
    "nachUpr",               // Начальник управления
};

private static readonly string[] DEPT_HEAD_POS = {
    "nachOtdelen",           // Начальник отделения (универсальная)
    "nachOtdelenNauch",      // Начальник научно-технического отдела 
    "nachOtdelen500",        // Начальник отделения 500кт (до миграции)
    "nachOtdelen600",        // Начальник отделения 600кт (до миграции)
    "zamNachOtd",            // Заместитель начальника отделения
};

private static readonly string[] SECTOR_HEAD_POS = {
    "nachSector",            // Начальник сектора (универсальная)
    "HeadSector",            // Начальник сектора 810кт (до миграции)
    "nachSect620",           // Начальник сектора 620кт (до миграции)
    "nachSect640",           // Начальник сектора 640кт (до миграции)
    "nachOtd",               // Начальник отдела
    "HeadDepartment",        // Начальник отдела 800кт
    "DepartmentManager",     // Начальник отдела 579
    "nachGroup",             // Начальник группы
    "nachServiceIT",         // Начальник службы ИТ
};

// Определение роли пользователя
private string GetUserRole( User user )
{
    try
    {
        var posNv = user.GetNamedValue( "Commission" );
        if( posNv == null ) return "regular";

        var key  = posNv.NameKey  ?? "";
        var name = posNv.ToString() ?? "";

        foreach( var k in ADMIN_POS )       if( key == k ) return "admin";
        foreach( var k in DEPT_HEAD_POS )   if( key == k ) return "headOfDept";
        foreach( var k in SECTOR_HEAD_POS ) if( key == k ) return "headOfSector";
        if( key.StartsWith( LEAD_ENGINEER_NAMEKEY_PREFIX, System.StringComparison.OrdinalIgnoreCase ) )
            return LEAD_ENGINEER_ROLE;

        // Запасной вариант по имени
        if( name.StartsWith( "Начальник отделения" ) ) return "headOfDept";
        if( name.StartsWith( "Начальник сектора" )   ) return "headOfSector";
    }
    catch { }
    return "regular";
}

private bool HasSectorScopeRole( string role )
{
    return role == "headOfSector" || role == LEAD_ENGINEER_ROLE;
}

private string GetUserStableKey( User user )
{
    if( user == null ) return "";
    return !string.IsNullOrEmpty( user.NameKey ) ? user.NameKey
         : !string.IsNullOrEmpty( user.AccountId ) ? user.AccountId
         : user.Id.ToString();
}

private bool IsTaskPrivate( InfoObject task )
{
    if( task == null ) return false;
    try { return task.GetValue<bool>( "IsPrivate" ); } catch { }
    try
    {
        var s = (task.GetString( "IsPrivate" ) ?? "").Trim();
        return s == "1" || s.Equals( "true", System.StringComparison.OrdinalIgnoreCase );
    }
    catch { }
    return false;
}

private bool CanUserSeeTask( InfoObject task, User currentUser )
{
    if( task == null ) return false;
    if( !IsTaskPrivate( task ) ) return true;
    if( currentUser == null ) return false;

    var curKey = GetUserStableKey( currentUser );
    string creatorKey = "";
    try { creatorKey = task.GetString( "Creator" ) ?? ""; } catch { }

    string assigneeKey = "";
    try
    {
        var assignee = task.GetUser( "Assignee" );
        assigneeKey = GetUserStableKey( assignee );
    }
    catch { }

    return ( !string.IsNullOrEmpty( creatorKey ) && curKey == creatorKey )
        || ( !string.IsNullOrEmpty( assigneeKey ) && curKey == assigneeKey );
}

private User FindUserByKeyOrNull( string userKey )
{
    if( string.IsNullOrEmpty( userKey ) ) return null;
    foreach( var u in Service.AllUsers )
    {
        if( u.IsGroup ) continue;
        if( GetUserStableKey( u ) == userKey ) return u;
    }
    return null;
}

private bool CanAssignUserInScope( User currentUser, string currentRole, User targetUser )
{
    if( currentUser == null || targetUser == null ) return false;
    if( currentRole == "admin" ) return true;

    var myCtx     = GetUserContext( currentUser );
    var targetCtx = GetUserContext( targetUser );

    if( currentRole == "headOfDept" )
        return IsWithinContext( targetCtx, myCtx );

    if( HasSectorScopeRole( currentRole ) )
        return targetCtx == myCtx;

    return currentUser.Id == targetUser.Id;
}

// Чтение Context пользователя
private string GetUserContext( User user )
{
    try
    {
        var s = user.GetString( "Context" );
        if( !string.IsNullOrEmpty( s ) ) return s.Trim();
    }
    catch { }
    try
    {
        var nv = user.GetNamedValue( "Context" );
        if( nv != null )
        {
            var s = nv.ToString()?.Trim() ?? "";
            if( !string.IsNullOrEmpty( s ) ) return s;
        }
    }
    catch { }
    return "";
}

// Числовой префикс Context
// "610кт" → 610
private int ContextNumber( string ctx )
{
    if( string.IsNullOrEmpty( ctx ) ) return 0;
    var sb = new System.Text.StringBuilder();
    foreach( var c in ctx ) { if( char.IsDigit( c ) ) sb.Append( c ); else break; }
    int n; int.TryParse( sb.ToString(), out n );
    return n;
}

// Суффикс после цифр: "610кт" → "кт"
private string ContextSuffix( string ctx )
{
    if( string.IsNullOrEmpty( ctx ) ) return "";
    var sb = new System.Text.StringBuilder();
    bool skipDigits = true;
    foreach( var c in ctx )
    {
        if( skipDigits && char.IsDigit( c ) ) continue;
        skipDigits = false;
        sb.Append( c );
    }
    return sb.ToString();
}

// Родительское отделение: "610кт" → "600кт"
private string GetDivisionContext( string ctx )
{
    int n = ContextNumber( ctx );
    if( n <= 0 ) return ctx;
    int divN = (n / 100) * 100;
    return divN.ToString() + ContextSuffix( ctx );
}

// Входит ли userCtx в область видимости ownerCtx?
// ownerCtx="600кт" покрывает "610кт", "620кт" и т.д.
private bool IsWithinContext( string userCtx, string ownerCtx )
{
    if( string.IsNullOrEmpty( userCtx ) ) return false;
    if( userCtx == ownerCtx ) return true;
    
    //Точечно: подструктура отделения 500кт (два зама 500.1 и 500.2 с нестандартными контекстами)
    
    if( ownerCtx == "500.1кт" && (userCtx == "510кт" || userCtx == "580кт")) return true;
    if( ownerCtx == "500.2кт" && (userCtx == "520кт" || userCtx == "530кт")) return true;
    if( ownerCtx == "500кт" && (userCtx == "500.1кт" || userCtx == "500.2кт")) return true; 

    return GetDivisionContext( userCtx ) == ownerCtx;
}

// Invoke: SetViewMode
private object DoSetViewMode( InfoObject obj, object inputParams )
{
    var mode = GetParamStr( inputParams );
    if( string.IsNullOrEmpty( mode ) ) mode = "my";
    obj.PropertyBag["KbViewMode"] = mode;
    return "OK";
}

// «Что нового»: персональная отметка о просмотренной версии хранится в пользовательском
// реестре PLM (per-user, переживает перезапуск, у пользователя есть права на свою ветку).
// Сигнатура версии вычисляется на клиенте по верхней записи списка (её дате) – ручной
// "бамп" не нужен: добавили новую запись сверху → сигнатура сменилась → кнопка снова пульсирует.
private object DoGetWhatsNewSeen()
{
    try { return Service.GetUserRegistryValue<string>( @"KanbanScreen\WhatsNewSeen", "" ) ?? ""; }
    catch { return ""; }
}

private object DoSetWhatsNewSeen( object inputParams )
{
    try
    {
        var sig = GetParamStr( inputParams ) ?? "";
        Service.SetUserRegistryValue<string>( @"KanbanScreen\WhatsNewSeen", sig );
        return "OK";
    }
    catch( Exception ex ) { return "ERROR:" + ex.Message; }
}

// Invoke: GetHierarchyInfo
private object DoGetHierarchyInfo( InfoObject obj, object inputParams )
{
    var currentUser = Service.GetCurrentUser();
    if( currentUser == null ) return "{}";

    var role      = GetUserRole( currentUser );
    var myContext = GetUserContext( currentUser );
    var viewMode  = (obj.PropertyBag["KbViewMode"] as string) ?? "my";

    var visibleUsers = new System.Collections.Generic.List<User>();
    var sectorsSet   = new System.Collections.Generic.SortedDictionary<string, bool>();
    var divisionsSet = new System.Collections.Generic.SortedDictionary<string, bool>();

    foreach( var u in Service.AllUsers )
    {
        if( u.IsGroup ) continue;
        var ctx = GetUserContext( u );

        bool include = false;
        if     ( role == "admin" )        include = true;
        else if( role == "headOfDept" )   include = IsWithinContext( ctx, myContext );
        else if( HasSectorScopeRole( role ) ) include = (ctx == myContext);

        if( !include ) continue;

        visibleUsers.Add( u );
        if( !string.IsNullOrEmpty( ctx ) )
        {
            var div = GetDivisionContext( ctx );
            if( !string.IsNullOrEmpty( div ) && div != ctx )
            {
                divisionsSet[div] = true;  // родительское отделение
                sectorsSet[ctx]   = true;  // ctx – реальный сектор (510кт и т.п.)
            }
            else
            {
                // ctx сам является отделением (500кт, 600кт...)
                // только в divisionsSet, иначе в секторном селекторе появляется
                // дублирующий пункт «Отделение 500кт»
                divisionsSet[ctx] = true;
            }
        }
    }

    var myKey = !string.IsNullOrEmpty( currentUser.NameKey ) ? currentUser.NameKey
              : !string.IsNullOrEmpty( currentUser.AccountId ) ? currentUser.AccountId
              : currentUser.Id.ToString();

    var sb = new System.Text.StringBuilder();
    sb.Append( "{" );
    sb.Append( "\"role\":\"" + JsonEscape( role ) + "\"," );
    sb.Append( "\"myKey\":\"" + JsonEscape( myKey ) + "\"," );
    sb.Append( "\"myContext\":\"" + JsonEscape( myContext ) + "\"," );
    sb.Append( "\"viewMode\":\"" + JsonEscape( viewMode ) + "\"," );

    sb.Append( "\"divisions\":[" );
    bool first = true;
    foreach( var div in divisionsSet.Keys )
    {
        if( div.IndexOf( "кт", System.StringComparison.OrdinalIgnoreCase ) < 0 ) continue;
        if( !first ) sb.Append( "," );
        sb.Append( "{\"key\":\"" + JsonEscape( div ) + "\","
                 + "\"name\":\"Отделение " + JsonEscape( div ) + "\"}" );
        first = false;
    }
    sb.Append( "]," );

    sb.Append( "\"sectors\":[" );
    first = true;
    foreach( var sec in sectorsSet.Keys )
    {
        if( !first ) sb.Append( "," );
        var label = divisionsSet.ContainsKey( sec )
            ? ("Отделение " + sec)
            : ("Сектор " + sec);
        sb.Append( "{\"key\":\"" + JsonEscape( sec ) + "\","
                 + "\"name\":\"" + JsonEscape( label ) + "\"}" );
        first = false;
    }
    sb.Append( "]," );

    visibleUsers.Sort( (a, b) => string.Compare( a.ToString() ?? "", b.ToString() ?? "", System.StringComparison.CurrentCultureIgnoreCase ) );

    sb.Append( "\"users\":[" );
    first = true;
    foreach( var u in visibleUsers )
    {
        if( !first ) sb.Append( "," );
        // string.IsNullOrEmpty нужен: u.NameKey="" не попадает под ?? (пустая != null)
        var rawKey = !string.IsNullOrEmpty( u.NameKey ) ? u.NameKey
                   : !string.IsNullOrEmpty( u.AccountId ) ? u.AccountId
                   : u.Id.ToString();
        var uKey     = JsonEscape( rawKey );
        var uName    = JsonEscape( u.ToString() ?? "" );
        var uCtx     = JsonEscape( GetUserContext( u ) );
        var uSubrole = JsonEscape( GetUserRole( u ) );
        sb.Append( "{\"key\":\"" + uKey + "\","
                 + "\"name\":\"" + uName + "\","
                 + "\"context\":\"" + uCtx + "\","
                 + "\"subrole\":\"" + uSubrole + "\"}" );
        first = false;
    }
    sb.Append( "]" );
    sb.Append( "}" );

    return sb.ToString();
}

// GetReport
// inputParams: "period|scopeMode"
// period: week | month | quarter | all
// scopeMode: "" (роль-default) | all | my | dept | sector | group:CTX | user:KEY
private object DoGetReport( object inputParams )
{
    var raw    = GetParamStr( inputParams );
    var parts  = ParsePipeArgs( raw, 2 );
    var period = parts.Length > 0 ? parts[0].Trim() : "month";
    var scope  = parts.Length > 1 ? parts[1].Trim() : "";
    if( string.IsNullOrEmpty( period ) ) period = "month";

    var container = Service.GetDataContainer( "All_Kanban_Tasks_Folder" );
    if( container == null ) return "ERROR:ContainerNotFound";

    var currentUser = Service.GetCurrentUser();
    var role        = GetUserRole( currentUser );

    // Regular всегда видит только свои задачи
    if( role == "regular" ) scope = "my";
    // Если scope не задан – дефолт по роли
    if( string.IsNullOrEmpty( scope ) )
        scope = role == "admin"         ? "all"
              : role == "headOfDept"    ? "dept"
              : HasSectorScopeRole( role ) ? "sector"
              : "my";

    var allowedIds = GetAllowedUserIdSet( currentUser, role, scope );

    var now  = DateTime.Now;
    DateTime from;
    DateTime to   = DateTime.MaxValue;
    switch( period )
    {
        case "week":    from = now.AddDays( -7 );   break;
        case "month":   from = now.AddMonths( -1 ); break;
        case "quarter": from = now.AddMonths( -3 ); break;
        case "custom":
            // custom|from|to|scope – парсим даты из частей
            from = DateTime.MinValue;
            {
                var rp = ParsePipeArgs( raw, 4 );
                if( rp.Length >= 3 )
                {
                    DateTime tmpFrom, tmpTo;
                    if( DateTime.TryParseExact( rp[1], "dd.MM.yyyy",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out tmpFrom ) )
                        from = tmpFrom;
                    if( DateTime.TryParseExact( rp[2], "dd.MM.yyyy",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out tmpTo ) )
                        to = tmpTo.Date.AddDays(1).AddTicks(-1); // конец дня
                }
            }
            break;
        default:        from = DateTime.MinValue;   break; // "all"
    }

    int totalTasks        = 0;
    int createdInPeriod   = 0;
    int completedInPeriod = 0;
    int overdue           = 0;
    int inProgress        = 0;

    var userStats   = new System.Collections.Generic.Dictionary<string, int[]>();
    var userContext = new System.Collections.Generic.Dictionary<string, string>();

    foreach( var task in container.RootInfoObjects )
    {
        var assignee = task.GetUser( "Assignee" );
        if( assignee == null ) continue;

        if( allowedIds != null && !allowedIds.Contains( assignee.Id.ToString() ) ) continue;
        if( IsTaskPrivate( task ) ) continue;

        totalTasks++;
        int status = GetStatusIndex( task );
        var aName  = assignee.ToString() ?? "?";
        var aCtx   = GetUserContext( assignee );

        if( !userStats.ContainsKey( aName ) )
        {
            userStats[aName]   = new int[4];
            userContext[aName] = aCtx;
        }
        userStats[aName][0]++;

        // Создана в периоде?
        try { if( task.DateCreated >= from && task.DateCreated <= to ) createdInPeriod++; } catch { }

        // Выполнена в периоде?
        try
        {
            var cd = task.GetValue<DateTime>( "CompletedDate" );
            if( cd != default(DateTime) && cd >= from && cd <= to )
            { completedInPeriod++; userStats[aName][1]++; }
        }
        catch { }

        // Просрочена? (не выполнена + срок прошёл)
        try
        {
            var dd = task.GetValue<DateTime>( "DueDate" );
            if( dd != default(DateTime) && dd < now && status != STATUS_DONE )
            { overdue++; userStats[aName][2]++; }
        }
        catch { }

        // В работе?
        if( status == 1 ) { inProgress++; userStats[aName][3]++; }
    }

    var sb = new System.Text.StringBuilder();
    sb.Append( "{" );
    sb.Append( "\"period\":\""          + JsonEscape( period ) + "\"," );
    sb.Append( "\"scope\":\""           + JsonEscape( scope )  + "\"," );
    sb.Append( "\"totalTasks\":"        + totalTasks           + "," );
    sb.Append( "\"createdInPeriod\":"   + createdInPeriod      + "," );
    sb.Append( "\"completedInPeriod\":" + completedInPeriod    + "," );
    sb.Append( "\"overdue\":"           + overdue              + "," );
    sb.Append( "\"inProgress\":"        + inProgress           + "," );
    sb.Append( "\"byUser\":[" );
    bool first = true;
    foreach( var kv in userStats )
    {
        if( !first ) sb.Append( "," );
        var ctx = userContext.ContainsKey( kv.Key ) ? userContext[kv.Key] : "";
        sb.Append( "{\"name\":\""     + JsonEscape( kv.Key ) + "\","
                 + "\"context\":\""   + JsonEscape( ctx )    + "\","
                 + "\"total\":"       + kv.Value[0]          + ","
                 + "\"completed\":"   + kv.Value[1]          + ","
                 + "\"overdue\":"     + kv.Value[2]          + ","
                 + "\"inProgress\":" + kv.Value[3]           + "}" );
        first = false;
    }
    sb.Append( "]}" );
    return sb.ToString();
}

// Набор Id пользователей для фильтрации в BeforeRender
// Возвращает null → «all» режим, показываем всё (только admin)
// Возвращает HashSet → фильтруем по Id исполнителя
private System.Collections.Generic.HashSet<string> GetAllowedUserIdSet(
    User currentUser, string role, string viewMode )
{
    // «my» и «regular» – только текущий пользователь
    if( role == "regular" || viewMode == "my" )
    {
        var myOnly = new System.Collections.Generic.HashSet<string>();
        myOnly.Add( currentUser.Id.ToString() );
        return myOnly;
    }

    if( viewMode == "myCreated" ) return null;

    if( viewMode == "all" && role == "admin" ) return null;

    // Для всех остальных режимов собираем набор БЕЗ предварительного добавления
    // currentUser – иначе его задачи попадают в чужой отдел/сектор
    var ids = new System.Collections.Generic.HashSet<string>();

    if( viewMode.StartsWith( "user:" ) )
    {
        var targetKey = viewMode.Substring( 5 );
        foreach( var u in Service.AllUsers )
            if( !u.IsGroup && ( !string.IsNullOrEmpty( u.NameKey ) && u.NameKey == targetKey
                              || !string.IsNullOrEmpty( u.AccountId ) && u.AccountId == targetKey
                              || u.Id.ToString() == targetKey ) )
                ids.Add( u.Id.ToString() );
        return ids;
    }

    if( viewMode.StartsWith( "group:" ) )
    {
        var targetCtx = viewMode.Substring( 6 );
        foreach( var u in Service.AllUsers )
        {
            if( u.IsGroup ) continue;
            var ctx = GetUserContext( u );
            bool allowed = false;
            if     ( role == "admin" )        allowed = IsWithinContext( ctx, targetCtx ) || ctx == targetCtx;
            else if( role == "headOfDept" )   allowed = IsWithinContext( ctx, targetCtx );
            else if( HasSectorScopeRole( role ) ) allowed = (ctx == targetCtx);
            if( allowed ) ids.Add( u.Id.ToString() );
        }
        return ids;
    }

    if( viewMode == "sector" && (HasSectorScopeRole( role ) || role == "headOfDept") )
    {
        var myCtx = GetUserContext( currentUser );
        foreach( var u in Service.AllUsers )
        {
            if( u.IsGroup ) continue;
            if( GetUserContext( u ) == myCtx ) ids.Add( u.Id.ToString() );
        }
        return ids;
    }

    if( viewMode == "dept" && role == "headOfDept" )
    {
        var myCtx = GetUserContext( currentUser );
        foreach( var u in Service.AllUsers )
        {
            if( u.IsGroup ) continue;
            if( IsWithinContext( GetUserContext( u ), myCtx ) ) ids.Add( u.Id.ToString() );
        }
        return ids;
    }

    return ids;
}

// GetTaskDetails
// inputParams: nameKey
// Возвращает полный JSON задачи для модального окна карточки
private object DoGetTaskDetails( object inputParams )
{
    var nameKey = GetParamStr( inputParams );
    if( string.IsNullOrEmpty( nameKey ) ) return "ERROR:EmptyId";

    var task = GetTaskByKeyOrNull( nameKey );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    int    status       = GetStatusIndex( task );
    string title        = task.GetString( "TaskName" )    ?? "";
    string details      = task.GetString( "TaskDetails" ) ?? "";
    string tags         = "";
    try { tags = task.GetString( "Tags" ) ?? ""; } catch { }
    string directumLink = "";
    try { directumLink = task.GetString( "DirectumLink" ) ?? ""; } catch { }
    string dueDate      = "";
    string createdAt    = "";
    string completedAt  = "";
    string originalDueDate = "";
    string assigneeName = "";
    string priorityKey  = "";
    string priorityName = "";
    bool   isPrivate    = IsTaskPrivate( task );
    int    returnCount  = 0;

    // Исполнитель
    try
    {
        var asg = task.GetUser( "Assignee" );
        if( asg != null )
            assigneeName = asg.ToString() ?? "";
    }
    catch { }

    // Приоритет
    try
    {
        var nv = task.GetNamedValue( "Priority" );
        if( nv != null )
        {
            priorityKey  = nv.NameKey ?? nv.Id.ToString();
            priorityName = nv.ToString() ?? "";
        }
    }
    catch { }

    // Даты
    try { var dd = task.GetValue<DateTime>( "DueDate" );
          if( dd != default(DateTime) ) dueDate = dd.ToString( "dd.MM.yyyy" ); } catch { }
    try { createdAt = task.DateCreated.ToString( "dd.MM.yyyy HH:mm" ); } catch { }
    try { var cd = task.GetValue<DateTime>( "CompletedDate" );
          if( cd != default(DateTime) ) completedAt = cd.ToString( "dd.MM.yyyy HH:mm" ); } catch { }
    try { var odd = task.GetValue<DateTime>( "OriginalDueDate" );
          if( odd != default(DateTime) ) originalDueDate = odd.ToString( "dd.MM.yyyy" ); } catch { }
    try { returnCount = task.GetValue<int>( "ReturnCount" ); } catch { }

    // Просроченность
    bool isOverdue = false;
    try
    {
        var dd2 = task.GetValue<DateTime>( "DueDate" );
        if( dd2 != default(DateTime) && dd2.Date < DateTime.Today && status != 3 )
            isOverdue = true;
    }
    catch { }

    // Права: создатель, имя создателя, полное редактирование
    bool   isOwner      = false;
    bool   canFullEdit  = true;
    string creatorName  = "";
    try
    {
        var creatorKey = task.GetString( "Creator" ) ?? "";
        var curUser    = Service.GetCurrentUser();
        if( string.IsNullOrEmpty( creatorKey ) )
        {
            // Legacy-задачи без Creator – разрешаем полное редактирование
            isOwner     = true;
            canFullEdit = true;
        }
        else if( curUser != null )
        {
            var curKey = string.IsNullOrEmpty( curUser.NameKey ) ? curUser.AccountId : curUser.NameKey;
            if( creatorKey == curKey )
            {
                isOwner     = true;
                canFullEdit = true;
            }
            else
            {
                // Начальники могут редактировать и удалять задачи подчинённых
                var curRole = GetUserRole( curUser );
                if( curRole == "admin" )
                {
                    isOwner     = true;
                    canFullEdit = true;
                }
                else if( curRole == "headOfDept" || HasSectorScopeRole( curRole ) )
                {
                    var creatorUser = FindUserByKeyOrNull( creatorKey );
                    var inScope = creatorUser != null && CanAssignUserInScope( curUser, curRole, creatorUser );
                    isOwner     = inScope;
                    canFullEdit = inScope;
                }
                else
                {
                    isOwner     = false;
                    canFullEdit = false;
                }
            }
        }
        else
        {
            isOwner     = false;
            canFullEdit = false;
        }
        // Имя создателя
        if( !string.IsNullOrEmpty( creatorKey ) )
        {
            try
            {
                foreach( var u in Service.AllUsers )
                {
                    var uKey = string.IsNullOrEmpty( u.NameKey ) ? u.AccountId : u.NameKey;
                    if( uKey == creatorKey )
                    {
                        creatorName = u.ToString() ?? "";
                        break;
                    }
                }
            }
            catch { }
        }
    }
    catch { }

    // Список приоритетов для dropdown
    var pSb   = new System.Text.StringBuilder();
    bool fp   = true;
    pSb.Append( "[" );
    try
    {
        var pc = Service.GetDataContainer( "Ref_KanbanPriority" );
        if( pc != null )
        {
            foreach( var p in pc.RootInfoObjects )
            {
                if( !fp ) pSb.Append( "," );
                var pk = p.NameKey ?? p.Id.ToString();
                var pn = p.ToString() ?? "";
                pSb.Append( "{\"key\":\"" + JsonEscape( pk ) + "\",\"name\":\"" + JsonEscape( pn ) + "\"}" );
                fp = false;
            }
        }
    }
    catch { }
    if( fp ) // запасной вариант
        pSb.Append( "{\"key\":\"High\",\"name\":\"Высокий\"},{\"key\":\"Medium\",\"name\":\"Средний\"},{\"key\":\"Low\",\"name\":\"Низкий\"},{\"key\":\"Urgent\",\"name\":\"Сверхсрочная\"}" );
    pSb.Append( "]" );

    // Список доступных тегов из справочника Ref_KanbanTags
    var tSb = new System.Text.StringBuilder();
    bool ft = true;
    tSb.Append( "[" );
    try
    {
        // Способ 1: NamedValues (именованные значения) через корневой путь
        var rootNv = Service.GetNamedValue( "Ref_KanbanTags" );
        if( rootNv != null )
        {
            foreach( var t in rootNv.AllChildren )
            {
                if( t.IsHiddenInUI ) continue;
                var name = t.ToString() ?? "";
                if( string.IsNullOrEmpty( name ) ) continue;
                if( !ft ) tSb.Append( "," );
                tSb.Append( "\"" + JsonEscape( name ) + "\"" );
                ft = false;
            }
        }
        // Способ 2 (fallback): InfoObjects в DataContainer
        if( ft )
        {
            var tc = Service.GetDataContainer( "Ref_KanbanTags" );
            if( tc != null )
            {
                foreach( var t in tc.RootInfoObjects )
                {
                    if( !ft ) tSb.Append( "," );
                    tSb.Append( "\"" + JsonEscape( t.ToString() ?? "" ) + "\"" );
                    ft = false;
                }
            }
        }
    }
    catch { }
    tSb.Append( "]" );

    // Пометить задачу как просмотренную (убирает бейдж «НОВАЯ» при следующем рендере)
    try
    {
        var curUser2 = Service.GetCurrentUser();
        if( curUser2 != null )
        {
            var ck = string.IsNullOrEmpty( curUser2.NameKey ) ? curUser2.AccountId : curUser2.NameKey;
            var seenBy = task.GetString( "SeenByList" ) ?? "";
            if( !string.IsNullOrEmpty( ck ) && seenBy.IndexOf( ck ) < 0 )
            {
                var editable = task.GetEditable();
                editable["SeenByList"] = string.IsNullOrEmpty( seenBy ) ? ck : seenBy + "," + ck;
                editable.Save();
            }
        }
    }
    catch { }

    var sb = new System.Text.StringBuilder();
    sb.Append( "{" );
    sb.Append( "\"nameKey\":\""      + JsonEscape( nameKey )      + "\"," );
    sb.Append( "\"title\":\""        + JsonEscape( title )         + "\"," );
    sb.Append( "\"status\":"         + status                      + "," );
    sb.Append( "\"priorityKey\":\""  + JsonEscape( priorityKey )   + "\"," );
    sb.Append( "\"priorityName\":\"" + JsonEscape( priorityName )  + "\"," );
    sb.Append( "\"assignee\":\""     + JsonEscape( assigneeName )  + "\"," );
    sb.Append( "\"dueDate\":\""      + JsonEscape( dueDate )       + "\"," );
    sb.Append( "\"originalDueDate\":\"" + JsonEscape( originalDueDate ) + "\"," );
    sb.Append( "\"returnCount\":"    + returnCount                 + "," );
    sb.Append( "\"isPrivate\":"      + (isPrivate ? "true" : "false") + "," );
    sb.Append( "\"details\":\""      + JsonEscape( details )       + "\"," );
    sb.Append( "\"directumLink\":\"" + JsonEscape( directumLink )  + "\"," );
    sb.Append( "\"createdAt\":\""    + JsonEscape( createdAt )     + "\"," );
    sb.Append( "\"completedAt\":\""  + JsonEscape( completedAt )   + "\"," );
    sb.Append( "\"isOwner\":"        + (isOwner ? "true" : "false")     + "," );
    sb.Append( "\"canFullEdit\":"   + (canFullEdit ? "true" : "false") + "," );
    sb.Append( "\"creatorName\":\""  + JsonEscape( creatorName )        + "\"," );
    sb.Append( "\"tags\":\""          + JsonEscape( tags )                + "\"," );
    sb.Append( "\"isOverdue\":"     + (isOverdue ? "true" : "false")    + "," );
    sb.Append( "\"availableTags\":"  + tSb.ToString()                    + "," );
    sb.Append( "\"priorities\":"     + pSb.ToString()                    + "," );
    sb.Append( "\"statusNames\":[\"Надо сделать\",\"В работе\",\"Ожидание\",\"Готово\"]," );

    // Список подчинённых для смены исполнителя (только для создателя задачи)
    sb.Append( "\"subordinates\":" );
    if( canFullEdit )
    {
        var curUser3 = Service.GetCurrentUser();
        var curRole3 = GetUserRole( curUser3 );
        if( curRole3 != "regular" )
        {
            var subList = new System.Collections.Generic.List<User>();
            var myCtx3  = GetUserContext( curUser3 );
            foreach( var u in Service.AllUsers )
            {
                if( u.IsGroup ) continue;
                var ctx = GetUserContext( u );
                bool include = false;
                if     ( curRole3 == "admin" )        include = true;
                else if( curRole3 == "headOfDept" )   include = IsWithinContext( ctx, myCtx3 );
                else if( HasSectorScopeRole( curRole3 ) ) include = (ctx == myCtx3);
                if( include ) subList.Add( u );
            }
            subList.Sort( (a, b) => string.Compare( a.ToString() ?? "", b.ToString() ?? "", System.StringComparison.CurrentCultureIgnoreCase ) );
            sb.Append( "[" );
            bool fs = true;
            foreach( var u in subList )
            {
                if( !fs ) sb.Append( "," );
                var rawK = !string.IsNullOrEmpty( u.NameKey ) ? u.NameKey
                         : !string.IsNullOrEmpty( u.AccountId ) ? u.AccountId
                         : u.Id.ToString();
                sb.Append( "{\"key\":\"" + JsonEscape( rawK ) + "\",\"name\":\"" + JsonEscape( u.ToString() ?? "" ) + "\"}" );
                fs = false;
            }
            sb.Append( "]" );
        }
        else
        {
            sb.Append( "[]" );
        }
    }
    else
    {
        sb.Append( "[]" );
    }

    // Ключ текущего исполнителя
    string assigneeKey = "";
    try
    {
        var asg2 = task.GetUser( "Assignee" );
        if( asg2 != null )
            assigneeKey = !string.IsNullOrEmpty( asg2.NameKey ) ? asg2.NameKey
                        : !string.IsNullOrEmpty( asg2.AccountId ) ? asg2.AccountId
                        : asg2.Id.ToString();
    }
    catch { }
    sb.Append( ",\"assigneeKey\":\"" + JsonEscape( assigneeKey ) + "\"" );

    sb.Append( "}" );
    return sb.ToString();
}

// SaveTask
// inputParams: "nameKey|title|status|priorityKey|dueDate|tags|assigneeKey|isPrivate|directumLink|details"
// details может содержать символы | – Split с лимитом 10 берёт всё остальное
private object DoSaveTask( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 10 );

    var nameKey       = parts.Length > 0 ? parts[0].Trim() : "";
    var title         = parts.Length > 1 ? parts[1].Trim() : "";
    var statusStr     = parts.Length > 2 ? parts[2].Trim() : "0";
    var prioKey       = parts.Length > 3 ? parts[3].Trim() : "";
    var dueDateStr    = parts.Length > 4 ? parts[4].Trim() : "";
    var tagsStr       = parts.Length > 5 ? parts[5].Trim() : "";
    var assigneeKeyIn = parts.Length > 6 ? parts[6].Trim() : "";
    var isPrivateIn    = "";
    var directumLinkIn = "";
    var detailsStr     = "";
    if( parts.Length > 9 )
    {
        // Новый формат: …|isPrivate|directumLink|details
        isPrivateIn    = parts[7].Trim();
        directumLinkIn = parts[8].Trim();
        detailsStr     = parts[9];    // НЕ Trim – пробелы в конце важны
    }
    else if( parts.Length > 8 )
    {
        // Прежний формат: …|isPrivate|details
        isPrivateIn = parts[7].Trim();
        detailsStr  = parts[8];
    }
    else
    {
        detailsStr  = parts.Length > 7 ? parts[7] : "";
    }

    if( string.IsNullOrEmpty( nameKey ) ) return "ERROR:EmptyId";

    var task = GetTaskByKeyOrNull( nameKey );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    int newStatus = 0;
    int.TryParse( statusStr, out newStatus );
    if( newStatus < 0 || newStatus > 3 ) newStatus = 0;

    // Проверка прав: создатель может редактировать всё, остальные – только статус
    var curUser = Service.GetCurrentUser();
    var curRole = GetUserRole( curUser );
    bool canFullEdit = true;
    string authorName = "";
    try
    {
        var creatorKey = task.GetString( "Creator" ) ?? "";
        if( curUser != null )
            authorName = curUser.ToString() ?? "";
        if( !string.IsNullOrEmpty( creatorKey ) && curUser != null )
        {
            var curKey = string.IsNullOrEmpty( curUser.NameKey ) ? curUser.AccountId : curUser.NameKey;
            if( creatorKey != curKey )
            {
                // Начальники могут редактировать задачи подчинённых
                if( curRole == "admin" )
                {
                    canFullEdit = true;
                }
                else if( curRole == "headOfDept" || HasSectorScopeRole( curRole ) )
                {
                    var creatorUser = FindUserByKeyOrNull( creatorKey );
                    canFullEdit = creatorUser != null && CanAssignUserInScope( curUser, curRole, creatorUser );
                }
                else
                {
                    canFullEdit = false;
                }
            }
        }
    }
    catch { }

    // Проверка названия – только для тех, кто редактирует содержимое
    if( canFullEdit && string.IsNullOrEmpty( title ) ) return "ERROR:EmptyTitle";

    // Сбор изменений для ChangeLog
    int    oldStatus    = GetStatusIndex( task );
    string oldTitle     = task.GetString( "TaskName" )    ?? "";
    string oldDetails   = task.GetString( "TaskDetails" ) ?? "";
    string oldTags      = "";
    bool   oldIsPrivate = IsTaskPrivate( task );
    try { oldTags = task.GetString( "Tags" ) ?? ""; } catch { }
    string oldDirectumLink = "";
    try { oldDirectumLink = task.GetString( "DirectumLink" ) ?? ""; } catch { }
    string oldPrioKey   = "";
    string oldPrioName  = "";
    string oldDueDate   = "";
    try { var nv = task.GetNamedValue( "Priority" );
          if( nv != null ) { oldPrioKey = nv.NameKey ?? nv.Id.ToString(); oldPrioName = nv.ToString() ?? ""; } } catch { }
    try { var dd2 = task.GetValue<DateTime>( "DueDate" );
          if( dd2 != default(DateTime) ) oldDueDate = dd2.ToString( "dd.MM.yyyy" ); } catch { }

    string[] stNames = new string[]{ "Надо сделать", "В работе", "Ожидание", "Готово" };

    // Применение изменений
    if( canFullEdit )
    {
        task["TaskName"]    = title;
        task["TaskDetails"] = detailsStr;
        task["Tags"]        = tagsStr;
        if( isPrivateIn == "1" || isPrivateIn == "0" )
            try { task["IsPrivate"] = (isPrivateIn == "1"); } catch { }

        // Приоритет
        if( !string.IsNullOrEmpty( prioKey ) )
        {
            var nv = Service.GetNamedValue( @"Ref_KanbanPriority\" + prioKey );
            if( nv != null ) task["Priority"] = nv;
        }

        // Срок
        if( !string.IsNullOrEmpty( dueDateStr ) )
        {
            DateTime dd;
            if( DateTime.TryParseExact( dueDateStr, "dd.MM.yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dd ) )
                task["DueDate"] = dd;
            else
                task["DueDate"] = null;
        }
        else
        {
            task["DueDate"] = null;
        }
    }

    // Ссылки Directum может добавлять/менять любой, кто видит задачу (как вложения и
    // комментарии), а не только автор — поэтому пишем вне блока canFullEdit.
    task["DirectumLink"] = directumLinkIn;

    if( oldStatus == STATUS_DONE && newStatus != STATUS_DONE )
        return "ERROR:ReturnReasonRequired";

    // Статус – могут менять все
    SetTaskStatus( task, newStatus );

    // Логика CompletedDate
    if( newStatus == STATUS_DONE && oldStatus != STATUS_DONE )
        task["CompletedDate"] = DateTime.Now;
    else if( newStatus != STATUS_DONE && oldStatus == STATUS_DONE )
        task["CompletedDate"] = null;

    // Смена исполнителя
    string oldAssigneeName = "";
    string newAssigneeName = "";
    bool assigneeChanged = false;
    if( canFullEdit && !string.IsNullOrEmpty( assigneeKeyIn ) )
    {
        try
        {
            var oldAsg = task.GetUser( "Assignee" );
            var oldAsgKey = GetUserStableKey( oldAsg );
            if( oldAsg != null )
            {
                oldAssigneeName = oldAsg.ToString() ?? "";
            }
            if( oldAsgKey != assigneeKeyIn )
            {
                var newUser = FindUserByKeyOrNull( assigneeKeyIn );
                if( newUser == null ) return "ERROR:AssigneeNotFound";
                if( !CanAssignUserInScope( curUser, curRole, newUser ) )
                    return "ERROR:AssigneeNotAllowed";
                task["Assignee"] = newUser;
                newAssigneeName = newUser.ToString() ?? "";
                assigneeChanged = true;
            }
        }
        catch { }
    }

    // Формирование записи ChangeLog
    try
    {
        var changes = new System.Text.StringBuilder();
        bool hasChange = false;
        changes.Append( "[" );

        if( canFullEdit && title != oldTitle )
        {
            if( hasChange ) changes.Append( "," );
            changes.Append( "{\"f\":\"Название\",\"o\":\"" + JsonEscape( oldTitle ) + "\",\"n\":\"" + JsonEscape( title ) + "\"}" );
            hasChange = true;
        }
        if( newStatus != oldStatus )
        {
            if( hasChange ) changes.Append( "," );
            var osn = oldStatus >= 0 && oldStatus < 4 ? stNames[oldStatus] : "?";
            var nsn = newStatus >= 0 && newStatus < 4 ? stNames[newStatus] : "?";
            changes.Append( "{\"f\":\"Статус\",\"o\":\"" + JsonEscape( osn ) + "\",\"n\":\"" + JsonEscape( nsn ) + "\"}" );
            hasChange = true;
        }
        if( canFullEdit && prioKey != oldPrioKey && !string.IsNullOrEmpty( prioKey ) )
        {
            if( hasChange ) changes.Append( "," );
            string newPrioName = "";
            try { var nvNew = Service.GetNamedValue( @"Ref_KanbanPriority\" + prioKey );
                  if( nvNew != null ) newPrioName = nvNew.ToString() ?? ""; } catch { }
            changes.Append( "{\"f\":\"Приоритет\",\"o\":\"" + JsonEscape( oldPrioName ) + "\",\"n\":\"" + JsonEscape( newPrioName ) + "\"}" );
            hasChange = true;
        }
        if( canFullEdit && dueDateStr != oldDueDate )
        {
            if( hasChange ) changes.Append( "," );
            changes.Append( "{\"f\":\"Срок\",\"o\":\"" + JsonEscape( oldDueDate ) + "\",\"n\":\"" + JsonEscape( dueDateStr ) + "\"}" );
            hasChange = true;
        }
        if( canFullEdit && detailsStr != oldDetails )
        {
            if( hasChange ) changes.Append( "," );
            changes.Append( "{\"f\":\"Описание\",\"o\":\"" + JsonEscape( TruncDesc( oldDetails ) ) + "\",\"n\":\"" + JsonEscape( TruncDesc( detailsStr ) ) + "\"}" );
            hasChange = true;
        }
        if( canFullEdit && tagsStr != oldTags )
        {
            if( hasChange ) changes.Append( "," );
            changes.Append( "{\"f\":\"Теги\",\"o\":\"" + JsonEscape( oldTags ) + "\",\"n\":\"" + JsonEscape( tagsStr ) + "\"}" );
            hasChange = true;
        }
        if( directumLinkIn != oldDirectumLink )
        {
            if( hasChange ) changes.Append( "," );
            changes.Append( "{\"f\":\"Ссылка Directum\",\"o\":\"" + JsonEscape( oldDirectumLink ) + "\",\"n\":\"" + JsonEscape( directumLinkIn ) + "\"}" );
            hasChange = true;
        }
        if( canFullEdit && (isPrivateIn == "1" || isPrivateIn == "0") && (isPrivateIn == "1") != oldIsPrivate )
        {
            if( hasChange ) changes.Append( "," );
            changes.Append( "{\"f\":\"Приватность\",\"o\":\"" + (oldIsPrivate ? "Приватная" : "Обычная") + "\",\"n\":\"" + (isPrivateIn == "1" ? "Приватная" : "Обычная") + "\"}" );
            hasChange = true;
        }
        if( assigneeChanged )
        {
            if( hasChange ) changes.Append( "," );
            changes.Append( "{\"f\":\"Исполнитель\",\"o\":\"" + JsonEscape( oldAssigneeName ) + "\",\"n\":\"" + JsonEscape( newAssigneeName ) + "\"}" );
            hasChange = true;
        }
        changes.Append( "]" );

        if( hasChange )
        {
            var entry = "{\"d\":\"" + DateTime.Now.ToString( "dd.MM.yyyy HH:mm" )
                      + "\",\"a\":\"" + JsonEscape( authorName )
                      + "\",\"c\":" + changes.ToString() + "}";

            var oldLog = task.GetString( "ChangeLog" ) ?? "";
            string newLog;
            if( string.IsNullOrEmpty( oldLog ) || oldLog.Trim() == "[]" )
                newLog = "[" + entry + "]";
            else
                newLog = oldLog.TrimEnd().TrimEnd( ']' ) + "," + entry + "]";

            task["ChangeLog"] = newLog;
        }
    }
    catch { /* ChangeLog не критичен – если атрибута нет, просто пропускаем */ }

    task.Save();
    return "OK";
}

// OpenDirectumLink
// inputParams: полная ссылка Directum, напр. http://server-d:8080/job.asp?sys=dir54&id=4083827
// Тихо (без браузера) тянем .isb с сервера обычным HTTP-запросом, сохраняем во временную
// папку Kanban и открываем ассоциированной программой (ISBExec) – страница Directum
// открывается напрямую. ComponentType (задание=10/документ=8/…) проставляет сам сервер.
// При недоступности сервера возвращаем FetchFailed – клиент откроет ссылку штатно (браузером).
// Возвращает: OK | ERROR:EmptyLink | ERROR:BadLink | ERROR:FetchFailed | ERROR:NoAssoc | ERROR:<текст>
private object DoOpenDirectumLink( object inputParams )
{
    var url = GetParamStr( inputParams );
    if( url != null ) url = url.Trim();
    if( string.IsNullOrEmpty( url ) ) return "ERROR:EmptyLink";

    string sys = null, id = null;
    try
    {
        var mSys = System.Text.RegularExpressions.Regex.Match(
            url, @"[?&]sys=([^&\s]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase );
        if( mSys.Success ) sys = System.Uri.UnescapeDataString( mSys.Groups[1].Value );

        var mId = System.Text.RegularExpressions.Regex.Match(
            url, @"[?&]id=([^&\s]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase );
        if( mId.Success ) id = System.Uri.UnescapeDataString( mId.Groups[1].Value );
    }
    catch { }

    if( string.IsNullOrEmpty( sys ) || string.IsNullOrEmpty( id ) )
        return "ERROR:BadLink";

    // Тихо скачиваем настоящий .isb прямо с сервера Directum обычным HTTP-запросом из кода –
    // без браузера и без ручного сохранения. Сервер сам подставляет правильный ComponentType
    // (задание = 10, документ = 8 и т.д.), поэтому одинаковые на вид ссылки на задание и на
    // документ открываются корректно. Учётные данные – текущего пользователя (NTLM/Kerberos).
    string isb = null;
    if( url.StartsWith( "http://", StringComparison.OrdinalIgnoreCase )
     || url.StartsWith( "https://", StringComparison.OrdinalIgnoreCase ) )
    {
        try
        {
            var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create( url );
            req.Method                = "GET";
            req.UseDefaultCredentials = true;
            req.AllowAutoRedirect     = true;
            req.Timeout               = 15000;
            req.UserAgent             = "KanbanSoyz";
            using( var resp = (System.Net.HttpWebResponse)req.GetResponse() )
            using( var sr   = new System.IO.StreamReader( resp.GetResponseStream(), System.Text.Encoding.GetEncoding( 1251 ) ) )
            {
                var body = sr.ReadToEnd();
                // Принимаем ответ, только если это действительно .isb-файл
                if( !string.IsNullOrEmpty( body )
                 && body.IndexOf( "Version=ISB",    StringComparison.OrdinalIgnoreCase ) >= 0
                 && body.IndexOf( "ComponentType=", StringComparison.OrdinalIgnoreCase ) >= 0 )
                {
                    isb = body;
                }
            }
        }
        catch { /* нет сети/сервера либо ответ не .isb – откат на штатное открытие в браузере */ }
    }

    // Сервер недоступен или вернул не .isb – пусть клиент откроет ссылку штатно (старый путь).
    if( string.IsNullOrEmpty( isb ) )
        return "ERROR:FetchFailed";

    try
    {
        var dir = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "KanbanFiles" );
        try { System.IO.Directory.CreateDirectory( dir ); } catch { }

        var safeId = id;
        try { foreach( var c in System.IO.Path.GetInvalidFileNameChars() ) safeId = safeId.Replace( c, '_' ); } catch { }

        var path = System.IO.Path.Combine( dir, safeId + ".isb" );
        // .isb – ANSI-файл (Windows-1251); содержимое здесь только ASCII, но кодировку
        // задаём явно, чтобы ISBExec гарантированно прочитал файл.
        System.IO.File.WriteAllText( path, isb, System.Text.Encoding.GetEncoding( 1251 ) );

        try
        {
            System.Diagnostics.Process.Start( path );
        }
        catch( System.ComponentModel.Win32Exception )
        {
            // Нет ассоциации .isb с ISBExec на этой машине (не установлен клиент Directum)
            return "ERROR:NoAssoc";
        }
        return "OK";
    }
    catch( Exception ex )
    {
        return "ERROR:" + ex.Message;
    }
}

// GetTaskHistory
// inputParams: nameKey
// Возвращает ChangeLog (JSON-массив записей изменений).
// Fallback: если ChangeLog пуст – показывает системные ревизии PLM (дата без деталей).
private object DoGetTaskHistory( object inputParams )
{
    var nameKey = GetParamStr( inputParams );
    if( string.IsNullOrEmpty( nameKey ) ) return "[]";

    var task = GetTaskByKeyOrNull( nameKey );
    if( task == null ) return "[]";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "[]";

    // Пробуем ChangeLog
    try
    {
        var log = task.GetString( "ChangeLog" ) ?? "";
        if( !string.IsNullOrEmpty( log ) && log.Trim() != "" && log.Trim() != "[]" )
            return log;
    }
    catch { }

    // Fallback: системные ревизии PLM (если ChangeLog ещё не настроен)
    try
    {
        var revisions = task.Revisions;
        if( revisions == null || revisions.Length == 0 ) return "[]";

        var sb = new System.Text.StringBuilder();
        sb.Append( "[" );
        for( int i = 0; i < revisions.Length; i++ )
        {
            if( i > 0 ) sb.Append( "," );
            string revDate = "";
            try { revDate = revisions[i].DateCreated.ToString( "dd.MM.yyyy HH:mm" ); } catch { }
            sb.Append( "{\"d\":\"" + JsonEscape( revDate ) + "\",\"a\":\"\",\"c\":[{\"f\":\"Ревизия " + (i + 1) + "\",\"o\":\"\",\"n\":\"\"}]}" );
        }
        sb.Append( "]" );
        return sb.ToString();
    }
    catch
    {
        return "[]";
    }
}
// Вложения через «Множество информационных объектов»
// Атрибут AttachedObjects (тип: Множество ИО) на шаблоне KanbanTask
private int GetAttachmentCount( InfoObject task )
{
    int n = 0;
    try
    {
        var objAttr = task.GetAttribute( "AttachedObjects" );
        if( objAttr != null )
        {
            var set = objAttr.LinkedInfoObjects.SafeToSet();
            if( set != null ) foreach( InfoObject o in set ) n++;
        }
    }
    catch { }
    try
    {
        var cntAttr = task.GetAttribute( "AttachedContainers" );
        if( cntAttr != null )
        {
            var set = cntAttr.LinkedDataContainers.SafeToSet();
            if( set != null ) foreach( DataContainer dc in set ) n++;
        }
    }
    catch { }
    return n;
}

// GetAttachments
// inputParams: "taskNameKey"
// Возвращает JSON: [{key, name, tmpl}]
private object DoGetAttachments( object inputParams )
{
    var taskKey = GetParamStr( inputParams );
    if( string.IsNullOrEmpty( taskKey ) ) return "[]";

    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "[]";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "[]";

    var sb    = new System.Text.StringBuilder();
    sb.Append( "[" );
    bool first = true;

    // InfoObjects
    try
    {
        var objAttr = task.GetAttribute( "AttachedObjects" );
        if( objAttr != null )
        {
            var set = objAttr.LinkedInfoObjects.SafeToSet();
            if( set != null )
            {
                foreach( InfoObject io in set )
                {
                    if( !first ) sb.Append( "," );
                    var key  = string.IsNullOrEmpty( io.NameKey ) ? io.Id.ToString() : io.NameKey;
                    var name = io.ToString() ?? key;
                    var tmpl = "";
                    try { tmpl = io.Template != null ? (io.Template.NameUI ?? "") : ""; } catch { }

                    // Признак локального файла: тело файла хранится прямо в Body
                    // самого объекта (Простой документ без версий). Для такого
                    // объекта доступны открытие файлом и редактирование (check-out).
                    var isFile = "0";
                    var lockState = "";   // "", "me", "other"
                    var lockBy = "";
                    try
                    {
                        var fd = io.GetValue<FileDesc>( "Body" );
                        if( fd != null && fd.IsFileBodyExists )
                        {
                            isFile = "1";
                            try
                            {
                                if( io.IsLocked )
                                {
                                    lockState = io.IsLockedByMe ? "me" : "other";
                                    if( !io.IsLockedByMe )
                                    {
                                        try { var ul = io.UserLock; if( ul != null && ul.User != null ) lockBy = ul.User.ToString() ?? ""; }
                                        catch { }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }

                    sb.Append( "{\"key\":\"" + JsonEscape( key  ) + "\","
                             + "\"name\":\"" + JsonEscape( name ) + "\","
                             + "\"tmpl\":\"" + JsonEscape( tmpl ) + "\","
                             + "\"isfile\":\"" + isFile + "\","
                             + "\"lock\":\"" + lockState + "\","
                             + "\"lockby\":\"" + JsonEscape( lockBy ) + "\","
                             + "\"type\":\"object\"}" );
                    first = false;
                }
            }
        }
    }
    catch { }

    // DataContainers
    try
    {
        var cntAttr = task.GetAttribute( "AttachedContainers" );
        if( cntAttr != null )
        {
            var set = cntAttr.LinkedDataContainers.SafeToSet();
            if( set != null )
            {
                foreach( DataContainer dc in set )
                {
                    if( dc == null ) continue;
                    if( !first ) sb.Append( "," );
                    var key  = dc.Id.ToString();
                    var name = dc.ToString() ?? key;
                    sb.Append( "{\"key\":\"" + JsonEscape( key  ) + "\","
                             + "\"name\":\"" + JsonEscape( name ) + "\","
                             + "\"tmpl\":\"\","
                             + "\"type\":\"container\"}" );
                    first = false;
                }
            }
        }
    }
    catch { }

    sb.Append( "]" );
    return sb.ToString();
}

// AddAttachment
// inputParams: "taskNameKey|objectKey"
// objectKey – NameKey объекта из результатов SearchObjects
private object DoAddAttachment( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var taskKey = parts[0].Trim();
    var objKey  = parts[1].Trim();
    if( string.IsNullOrEmpty( taskKey ) || string.IsNullOrEmpty( objKey ) )
        return "ERROR:EmptyParams";

    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    // Ищем объект через SearchOperation по NameKey
    var target = FindInfoObjectByKey( objKey );
    if( target == null ) return "ERROR:ObjectNotFound";

    // Проверка дубликатов, лимит и добавление
    try
    {
        var editTask = task.GetEditable();
        var attr     = editTask.GetAttribute( "AttachedObjects" );
        var set      = attr.LinkedInfoObjects.SafeToSet();
        foreach( InfoObject io in set )
        {
            var ioKey = string.IsNullOrEmpty( io.NameKey ) ? io.Id.ToString() : io.NameKey;
            if( ioKey == objKey ) return "ERROR:AlreadyAttached";
        }
        if( GetAttachmentCount( editTask ) >= 50 ) return "ERROR:LimitReached";
        set.Add( target );
        attr.LinkedInfoObjects = set;
        AppendAttachmentChangeLog( editTask, "Добавлен объект", target.ToString() ?? objKey );
        using( Service.EnterNewGroupOperation() )
        {
            editTask.Save();
            Service.SaveChanges();
        }
        return "OK";
    }
    catch( Exception ex ) { return "ERROR:" + ex.Message; }
}

// RemoveAttachment
// inputParams: "taskNameKey|objectKey"
private object DoRemoveAttachment( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var taskKey = parts[0].Trim();
    var objKey  = parts[1].Trim();

    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    try
    {
        var editTask = task.GetEditable();
        var attr     = editTask.GetAttribute( "AttachedObjects" );
        var set      = attr.LinkedInfoObjects.SafeToSet();
        InfoObject target = null;
        foreach( InfoObject io in set )
        {
            var ioKey = string.IsNullOrEmpty( io.NameKey ) ? io.Id.ToString() : io.NameKey;
            if( ioKey == objKey ) { target = io; break; }
        }
        if( target == null ) return "ERROR:NotFound";

        var removedName = target.ToString() ?? objKey;
        set.Remove( target );
        attr.LinkedInfoObjects = set;
        AppendAttachmentChangeLog( editTask, "Удалён объект", removedName );
        using( Service.EnterNewGroupOperation() )
        {
            editTask.Save();
            Service.SaveChanges();
        }
        return "OK";
    }
    catch( Exception ex ) { return "ERROR:" + ex.Message; }
}

// SearchObjects
// inputParams: "query" – поиск по отображаемому имени (CONTAINS, без шаблонного фильтра)
// Возвращает JSON: [{key, name, tmpl}]
private object DoSearchObjects( object inputParams )
{
    var query = GetParamStr( inputParams );
    if( string.IsNullOrEmpty( query ) || query.Length < 2 ) return "[]";

    try
    {
        var searchOp = new SearchOperation( EntityIdentifier.InfoObject, "KbAttSearch" );
        var filter   = new SearchInfoObjectFilterItem();
        filter.PropertyId = SearchConditionFilterItem.SystemPropertyId.Name;
        filter.OperatorId = RelationalOperator.Contains;
        filter.Argument1  = query;
        searchOp.SearchExpressionTree = filter;
        searchOp.MaxSQLResultsCount   = 50;
        searchOp.MaxResultsCount      = 20;
        searchOp.Execute( false );

        if( searchOp.FoundObjects == null ) return "[]";

        var sb    = new System.Text.StringBuilder();
        sb.Append( "[" );
        int count = 0;
        foreach( var found in searchOp.FoundObjects.OfType<InfoObject>() )
        {
            var key  = string.IsNullOrEmpty( found.NameKey ) ? found.Id.ToString() : found.NameKey;
            if( string.IsNullOrEmpty( key ) ) continue;
            if( count > 0 ) sb.Append( "," );
            var name = found.ToString() ?? key;
            var tmpl = "";
            try { tmpl = found.Template != null ? (found.Template.NameUI ?? "") : ""; } catch { }
            sb.Append( "{\"key\":\"" + JsonEscape( key  ) + "\","
                     + "\"name\":\"" + JsonEscape( name ) + "\","
                     + "\"tmpl\":\"" + JsonEscape( tmpl ) + "\"}" );
            count++;
        }
        sb.Append( "]" );
        return sb.ToString();
    }
    catch { return "[]"; }
}

// OpenObject
// inputParams: "taskNameKey|objectKey"
// Находит объект в коллекции задачи и открывает в панели свойств PLM
private object DoOpenObject( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var taskKey = parts[0].Trim();
    var objKey  = parts[1].Trim();

    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    try
    {
        var attr = task.GetAttribute( "AttachedObjects" );
        var set  = attr.LinkedInfoObjects.SafeToSet();
        foreach( InfoObject io in set )
        {
            var ioKey = string.IsNullOrEmpty( io.NameKey ) ? io.Id.ToString() : io.NameKey;
            if( ioKey == objKey )
            {
                // Локальный файл (Простой документ без версий) хранит тело прямо
                // в Body на самом объекте - открываем файл сразу в ассоциированном
                // приложении, минуя карточку свойств. У Технического документа
                // Body лежит на версии (ActualVersion), не на главном объекте, -
                // для него остаётся штатное открытие карточки.
                try
                {
                    var fd = io.GetValue<FileDesc>( "Body" );
                    if( fd != null && fd.IsFileBodyExists )
                    {
                        var name = fd.OriginalName;
                        if( string.IsNullOrEmpty( name ) )
                            name = io.GetString( "FileName" )
                                ?? io.GetString( "DocumentName" )
                                ?? "file";
                        var dir = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "KanbanFiles" );
                        try { System.IO.Directory.CreateDirectory( dir ); } catch { }
                        var path = fd.WriteFileTo( dir, name );
                        System.Diagnostics.Process.Start( path );
                        return "OK";
                    }
                }
                catch { /* не вышло открыть файлом - падаем на карточку */ }

                Service.UI.OpenPropertiesPane( io );
                return "OK";
            }
        }
        return "ERROR:ObjectNotFound";
    }
    catch( Exception ex ) { return "ERROR:" + ex.Message; }
}

// ─── Редактирование локального файла (check-out / check-in) ───
// Файл (Простой документ без версий) скачивается в стабильную папку
// %TEMP%\KanbanEdit\<id>\<name>, открывается в ассоциированном приложении,
// объект блокируется долговременной блокировкой. По кнопке "Сохранить в PLM"
// изменённый файл заливается обратно в Body и блокировка снимается.

// Находит вложенный ИО задачи по ключу
private InfoObject FindAttachedObject( InfoObject task, string objKey )
{
    try
    {
        var attr = task.GetAttribute( "AttachedObjects" );
        var set  = attr.LinkedInfoObjects.SafeToSet();
        foreach( InfoObject io in set )
        {
            var k = string.IsNullOrEmpty( io.NameKey ) ? io.Id.ToString() : io.NameKey;
            if( k == objKey ) return io;
        }
    }
    catch { }
    return null;
}

// Стабильный путь локальной копии для редактирования
private string GetEditFilePath( InfoObject io )
{
    string name = null;
    try { var fd = io.GetValue<FileDesc>( "Body" ); if( fd != null ) name = fd.OriginalName; } catch { }
    if( string.IsNullOrEmpty( name ) )
        name = io.GetString( "FileName" ) ?? io.GetString( "DocumentName" ) ?? "file";
    // Убираем недопустимые символы из имени
    try { foreach( var c in System.IO.Path.GetInvalidFileNameChars() ) name = name.Replace( c, '_' ); } catch { }
    var dir = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "KanbanEdit", io.Id.ToString() );
    return System.IO.Path.Combine( dir, name );
}

// EditLocalFile: "taskNameKey|objectKey"
// Блокирует объект, скачивает файл, открывает в ассоциированном приложении.
private object DoEditLocalFile( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var task = GetTaskByKeyOrNull( parts[0].Trim() );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    var io = FindAttachedObject( task, parts[1].Trim() );
    if( io == null ) return "ERROR:ObjectNotFound";

    FileDesc fd = null;
    try { fd = io.GetValue<FileDesc>( "Body" ); } catch { }
    if( fd == null || !fd.IsFileBodyExists ) return "ERROR:NotLocalFile";

    // Проверка чужой блокировки
    try
    {
        if( io.IsLocked && !io.IsLockedByMe )
        {
            var who = "";
            try { var ul = io.UserLock; if( ul != null && ul.User != null ) who = ul.User.ToString() ?? ""; } catch { }
            return "ERROR:LockedByOther:" + who;
        }
    }
    catch { }

    try
    {
        if( !io.IsLocked ) io.Lock();
    }
    catch( Exception lex ) { return "ERROR:LockFailed:" + lex.Message; }

    try
    {
        var path = GetEditFilePath( io );
        var dir  = System.IO.Path.GetDirectoryName( path );
        try { System.IO.Directory.CreateDirectory( dir ); } catch { }
        var name = System.IO.Path.GetFileName( path );
        var written = fd.WriteFileTo( dir, name );
        System.Diagnostics.Process.Start( written );
        return "OK";
    }
    catch( Exception ex )
    {
        try { if( io.IsLockedByMe ) io.Unlock(); } catch { }
        Service.HandleException( ex, "Ошибка открытия файла на редактирование" );
        return "ERROR:" + ex.Message;
    }
}

// SaveLocalFileEdit: "taskNameKey|objectKey"
// Заливает изменённый локальный файл обратно в Body и снимает блокировку.
private object DoSaveLocalFileEdit( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var task = GetTaskByKeyOrNull( parts[0].Trim() );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    var io = FindAttachedObject( task, parts[1].Trim() );
    if( io == null ) return "ERROR:ObjectNotFound";

    try
    {
        if( io.IsLocked && !io.IsLockedByMe ) return "ERROR:LockedByOther";
    }
    catch { }

    var path = GetEditFilePath( io );
    if( !System.IO.File.Exists( path ) ) return "ERROR:LocalFileMissing";

    try
    {
        // Файл может быть всё ещё открыт в Word/Excel/CAD (эксклюзивная блокировка
        // на запись). Читаем его через FileShare.ReadWrite в память - так чтение
        // не конфликтует с открытым приложением. Заливаем снимок в Body.
        var fileName = System.IO.Path.GetFileName( path );
        DateTime created  = DateTime.Now, modified = DateTime.Now;
        try { created  = System.IO.File.GetCreationTime( path );  } catch { }
        try { modified = System.IO.File.GetLastWriteTime( path ); } catch { }

        var editIo = io.GetEditable();
        bool uploaded = false;
        try
        {
            byte[] bytes;
            using( var fs = new System.IO.FileStream( path, System.IO.FileMode.Open,
                       System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite ) )
            using( var ms = new System.IO.MemoryStream() )
            {
                fs.CopyTo( ms );
                bytes = ms.ToArray();
            }
            var upStream = new System.IO.MemoryStream( bytes );
            editIo.GetAttribute( "Body" ).Value.UploadFile( upStream, fileName, created, modified );
            uploaded = true;
        }
        catch( System.IO.IOException )
        {
            // Файл занят так, что даже share-чтение не вышло - просим закрыть.
            return "ERROR:FileBusy";
        }

        if( !uploaded ) return "ERROR:ReadFailed";

        using( Service.EnterNewGroupOperation() )
        {
            editIo.Save();
            Service.SaveChanges();
        }
        try { if( editIo.IsLockedByMe ) editIo.Unlock(); }
        catch { try { if( io.IsLockedByMe ) io.Unlock(); } catch { } }
        try { System.IO.File.Delete( path ); } catch { }
        return "OK";
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "Ошибка сохранения файла в PLM" );
        return "ERROR:" + ex.Message;
    }
}

// CancelLocalFileEdit: "taskNameKey|objectKey"
// Снимает блокировку без сохранения, удаляет временную копию.
private object DoCancelLocalFileEdit( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var task = GetTaskByKeyOrNull( parts[0].Trim() );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    var io = FindAttachedObject( task, parts[1].Trim() );
    if( io == null ) return "ERROR:ObjectNotFound";

    try { if( io.IsLockedByMe ) io.Unlock(); } catch { }
    try { var p = GetEditFilePath( io ); if( System.IO.File.Exists( p ) ) System.IO.File.Delete( p ); } catch { }
    return "OK";
}

// PickObject
// Открывает нативный PLM-диалог выбора объекта (аналог кнопки «Добавить»
// в атрибуте «Множество ИО»). Возвращает "key|name|tmpl" или "CANCELLED".
private object DoPickObject( object inputParams )
{
    try
    {
        var p = new SelectInfoObjectParams
        {
            Caption         = "Выбор объекта для прикрепления к задаче",
            UseServerSearch = true
        };

        var obj = Service.UI.SelectInfoObject( p );
        if( obj == null ) return "CANCELLED";

        var key  = string.IsNullOrEmpty( obj.NameKey ) ? obj.Id.ToString() : obj.NameKey;
        var name = (obj.ToString() ?? key).Replace( "|", "" );
        var tmpl = (obj.Template != null ? (obj.Template.NameUI ?? "") : "").Replace( "|", "" );

        return key + "|" + name + "|" + tmpl;
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "Ошибка при вызове диалога выбора объекта" );
        return "CANCELLED";
    }
}

// PickObjects
// inputParams: "" (не используется)
// Открывает PLM-диалог через дерево с мультивыбором (BrowseForInfoObjects),
// Validator отсекает версии/исполнения по NameKey шаблона.
// Возвращает JSON-массив [{key, name, tmpl}] или "CANCELLED"
private object DoPickObjects( object inputParams )
{
    try
    {
        var browseParams = new MultiSelectInfoObjectParams
        {
            Validator = ( io ) =>
            {
                if( io == null || io.Template == null ) return false;
                var tKey = io.Template.NameKey ?? "";
                if( tKey.EndsWith( "Version" )              ) return false;
                if( tKey.Contains( "VersionConfiguration" ) ) return false;
                if( tKey.StartsWith( "ProductExecution" )   ) return false;
                return true;
            }
        };

        var selected = Service.UI.BrowseForInfoObjects( browseParams );
        if( selected == null || !selected.Any() ) return "CANCELLED";

        var sb    = new System.Text.StringBuilder();
        sb.Append( "[" );
        int count = 0;
        foreach( var item in selected )
        {
            if( item == null ) continue;
            var tn   = "";
            try { tn = item.Template != null ? ( item.Template.NameUI ?? "" ) : ""; } catch { }
            var key  = string.IsNullOrEmpty( item.NameKey ) ? item.Id.ToString() : item.NameKey;
            var name = item.ToString() ?? key;
            if( count > 0 ) sb.Append( "," );
            sb.Append( "{\"key\":\"" + JsonEscape( key  ) + "\","
                     + "\"name\":\"" + JsonEscape( name ) + "\","
                     + "\"tmpl\":\"" + JsonEscape( tn   ) + "\"}" );
            count++;
        }
        sb.Append( "]" );
        if( count == 0 ) return "CANCELLED";
        return sb.ToString();
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "Ошибка при открытии диалога выбора объектов" );
        return "CANCELLED";
    }
}

// PickAndAttach
// inputParams: "taskNameKey"
// Открывает PLM-диалог через дерево с мультивыбором и прикрепляет все выбранные объекты.
// Validator отсекает версии/исполнения. Дублей не создаёт.
private object DoPickAndAttach( object inputParams )
{
    var taskKey = GetParamStr( inputParams );
    if( string.IsNullOrEmpty( taskKey ) ) return "ERROR:EmptyParams";

    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    try
    {
        var browseParams = new MultiSelectInfoObjectParams
        {
            Validator = ( io ) =>
            {
                if( io == null || io.Template == null ) return false;
                var tKey = io.Template.NameKey ?? "";
                if( tKey.EndsWith( "Version" )              ) return false;
                if( tKey.Contains( "VersionConfiguration" ) ) return false;
                if( tKey.StartsWith( "ProductExecution" )   ) return false;
                return true;
            }
        };

        var selected = Service.UI.BrowseForInfoObjects( browseParams );
        if( selected == null || !selected.Any() ) return "CANCELLED";

        var editTask   = task.GetEditable();
        var attr       = editTask.GetAttribute( "AttachedObjects" );
        var currentSet = attr.LinkedInfoObjects.SafeToSet();

        // Собираем ключи уже существующих вложений для проверки дубликатов
        var existingKeys = new System.Collections.Generic.HashSet<string>();
        foreach( InfoObject io in currentSet )
        {
            var k = string.IsNullOrEmpty( io.NameKey ) ? io.Id.ToString() : io.NameKey;
            existingKeys.Add( k );
        }

        var addedItems = new System.Collections.Generic.List<string>();
        int totalCnt = GetAttachmentCount( editTask );
        foreach( var newItem in selected )
        {
            if( newItem == null ) continue;
            var itemKey = string.IsNullOrEmpty( newItem.NameKey ) ? newItem.Id.ToString() : newItem.NameKey;
            if( existingKeys.Contains( itemKey ) ) continue; // пропускаем дубликаты
            if( totalCnt >= 50 ) return "ERROR:LimitReached";
            currentSet.Add( newItem );
            existingKeys.Add( itemKey );
            addedItems.Add( newItem.ToString() ?? itemKey );
            totalCnt++;
        }
        if( addedItems.Count == 0 ) return "CANCELLED";
        attr.LinkedInfoObjects = currentSet.ToArray();
        foreach( var name in addedItems )
            AppendAttachmentChangeLog( editTask, "Добавлен объект", name );
        using( Service.EnterNewGroupOperation() )
        {
            editTask.Save();
            Service.SaveChanges();
        }
        return "OK";
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "Ошибка при прикреплении объектов" );
        return "ERROR:" + ex.Message;
    }
}

// PickContainers
// Открывает нативный PLM-диалог выбора папок/проектов (DataContainer).
// Возвращает JSON-массив [{key, name, type:"container"}] или "CANCELLED".
// Используется панелью создания задачи (выбор без немедленного сохранения).
private object DoPickContainers( object inputParams )
{
    try
    {
        var browseParams = new MultiSelectDataContainerParams();
        var selected     = Service.UI.SelectDataContainers( browseParams );
        if( selected == null || !selected.Any() ) return "CANCELLED";

        var sb    = new System.Text.StringBuilder();
        sb.Append( "[" );
        bool first = true;
        foreach( var dc in selected )
        {
            if( dc == null ) continue;
            if( !first ) sb.Append( "," );
            var key  = dc.Id.ToString();
            var name = dc.ToString() ?? key;
            sb.Append( "{\"key\":\"" + JsonEscape( key  ) + "\","
                     + "\"name\":\"" + JsonEscape( name ) + "\","
                     + "\"type\":\"container\"}" );
            first = false;
        }
        sb.Append( "]" );
        return sb.ToString();
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "Ошибка при выборе контейнеров" );
        return "ERROR:" + ex.Message;
    }
}

// PickAndAttachContainer
// Открывает диалог и сразу сохраняет выбранные контейнеры в задачу.
// inputParams: taskNameKey
private object DoPickAndAttachContainer( object inputParams )
{
    var taskKey = GetParamStr( inputParams );
    if( string.IsNullOrEmpty( taskKey ) ) return "ERROR:EmptyParams";

    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    try
    {
        var browseParams = new MultiSelectDataContainerParams();
        var selected     = Service.UI.SelectDataContainers( browseParams );
        if( selected == null || !selected.Any() ) return "CANCELLED";

        var editTask   = task.GetEditable();
        var attr       = editTask.GetAttribute( "AttachedContainers" );
        var currentSet = attr.LinkedDataContainers.SafeToSet();

        // Собираем Id уже существующих контейнеров для проверки дубликатов
        var existingIds = new System.Collections.Generic.HashSet<ulong>();
        foreach( DataContainer dc in currentSet )
            existingIds.Add( dc.Id );

        var addedNames = new System.Collections.Generic.List<string>();
        int totalCntC = GetAttachmentCount( editTask );
        foreach( var dc in selected )
        {
            if( dc == null ) continue;
            if( existingIds.Contains( dc.Id ) ) continue; // пропускаем дубликаты
            if( totalCntC >= 50 ) return "ERROR:LimitReached";
            currentSet.Add( dc );
            existingIds.Add( dc.Id );
            addedNames.Add( dc.ToString() ?? dc.Id.ToString() );
            totalCntC++;
        }
        if( addedNames.Count == 0 ) return "CANCELLED";
        attr.LinkedDataContainers = currentSet.ToArray();
        foreach( var name in addedNames )
            AppendAttachmentChangeLog( editTask, "Добавлен контейнер", name );
        using( Service.EnterNewGroupOperation() )
        {
            editTask.Save();
            Service.SaveChanges();
        }
        return "OK";
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "Ошибка при прикреплении папок/проектов" );
        return "ERROR:" + ex.Message;
    }
}

// AddContainer
// Прикрепляет контейнер по Id (используется после создания задачи).
// inputParams: "taskNameKey|containerId"
private object DoAddContainer( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var taskKey   = parts[0].Trim();
    var contIdStr = parts[1].Trim();

    if( string.IsNullOrEmpty( taskKey ) || string.IsNullOrEmpty( contIdStr ) )
        return "ERROR:EmptyParams";

    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    uint contId;
    if( !uint.TryParse( contIdStr, out contId ) ) return "ERROR:InvalidId";

    try
    {
        var dc = Service.GetDataContainer( contId );
        if( dc == null ) return "ERROR:ContainerNotFound";

        var editTask = task.GetEditable();
        var attr     = editTask.GetAttribute( "AttachedContainers" );
        var set      = attr.LinkedDataContainers.SafeToSet();
        foreach( DataContainer existing in set )
        {
            if( existing.Id == contId ) return "ERROR:AlreadyAttached";
        }
        if( GetAttachmentCount( editTask ) >= 50 ) return "ERROR:LimitReached";
        set.Add( dc );
        attr.LinkedDataContainers = set.ToArray();
        AppendAttachmentChangeLog( editTask, "Добавлен контейнер", dc.ToString() ?? contIdStr );
        using( Service.EnterNewGroupOperation() )
        {
            editTask.Save();
            Service.SaveChanges();
        }
        return "OK";
    }
    catch( Exception ex ) { return "ERROR:" + ex.Message; }
}

// RemoveContainer
// Открепляет контейнер по Id.
// inputParams: "taskNameKey|containerId"
private object DoRemoveContainer( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var taskKey   = parts[0].Trim();
    var contIdStr = parts[1].Trim();

    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    try
    {
        var editTask = task.GetEditable();
        var attr     = editTask.GetAttribute( "AttachedContainers" );
        var set      = attr.LinkedDataContainers.SafeToSet();
        DataContainer target = null;
        foreach( DataContainer dc in set )
        {
            if( dc.Id.ToString() == contIdStr ) { target = dc; break; }
        }
        if( target == null ) return "ERROR:NotFound";
        var removedName = target.ToString() ?? contIdStr;
        set.Remove( target );
        attr.LinkedDataContainers = set.ToArray();
        AppendAttachmentChangeLog( editTask, "Удалён контейнер", removedName );
        using( Service.EnterNewGroupOperation() )
        {
            editTask.Save();
            Service.SaveChanges();
        }
        return "OK";
    }
    catch( Exception ex ) { return "ERROR:" + ex.Message; }
}

// OpenContainer
// Открывает DataContainer в PLM-интерфейсе.
// inputParams: "taskNameKey|containerId"
private object DoOpenContainer( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var taskKey   = parts[0].Trim();
    var contIdStr = parts[1].Trim();

    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    try
    {
        var attr = task.GetAttribute( "AttachedContainers" );
        var set  = attr.LinkedDataContainers.SafeToSet();
        foreach( DataContainer dc in set )
        {
            if( dc.Id.ToString() == contIdStr )
            {
                Service.UI.OpenPropertiesPane( dc );
                return "OK";
            }
        }
        return "ERROR:ContainerNotFound";
    }
    catch( Exception ex ) { return "ERROR:" + ex.Message; }
}

// PickLocalFiles
// Открывает Windows OpenFileDialog (мультивыбор). Возвращает JSON-массив
// [{path, name, size}] или "CANCELLED".
private object DoPickLocalFiles( object inputParams )
{
    try
    {
        using( var ofd = new System.Windows.Forms.OpenFileDialog() )
        {
            ofd.Title       = "Выберите файлы для прикрепления";
            ofd.Filter      = "Все файлы (*.*)|*.*";
            ofd.Multiselect = true;
            if( ofd.ShowDialog() != System.Windows.Forms.DialogResult.OK ) return "CANCELLED";
            var files = ofd.FileNames;
            if( files == null || files.Length == 0 ) return "CANCELLED";

            var sb = new System.Text.StringBuilder();
            sb.Append( "[" );
            bool first = true;
            foreach( var p in files )
            {
                if( string.IsNullOrEmpty( p ) ) continue;
                long size = 0;
                try { size = new System.IO.FileInfo( p ).Length; } catch { }
                var name = System.IO.Path.GetFileName( p );
                if( !first ) sb.Append( "," );
                sb.Append( "{\"path\":\"" + JsonEscape( p    ) + "\","
                         + "\"name\":\""  + JsonEscape( name ) + "\","
                         + "\"size\":"    + size + "}" );
                first = false;
            }
            sb.Append( "]" );
            return sb.ToString();
        }
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "Ошибка диалога выбора файла" );
        return "ERROR:" + ex.Message;
    }
}

// PickStorageContainer
// Открывает PLM-диалог выбора папки/проекта для размещения нового документа.
// Возвращает: "containerId|displayName" или "CANCELLED".
private object DoPickStorageContainer( object inputParams )
{
    try
    {
        uint lastId = 0;
        try
        {
            var v = Service.GetUserRegistryValue( @"UserSettings\Kanban\LastFileContainer", 0 );
            if( v != null ) lastId = (uint)Convert.ToInt32( v );
        }
        catch { }

        var p = new SelectDataContainerParams
        {
            Caption = "Выберите папку для размещения файла"
        };
        if( lastId > 0 )
        {
            try { p.SelectedDataContainer = Service.GetDataContainer( lastId ); } catch { }
        }

        var dc = Service.UI.SelectDataContainer( p );
        if( dc == null ) return "CANCELLED";

        try { Service.SetUserRegistryValue( @"UserSettings\Kanban\LastFileContainer", (int)dc.Id ); } catch { }
        return dc.Id.ToString() + "|" + ( dc.ToString() ?? dc.Id.ToString() );
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "Ошибка выбора контейнера" );
        return "ERROR:" + ex.Message;
    }
}

// AttachLocalFile
// inputParams: "taskNameKey|filePath|containerIdOrEmpty"
// Создаёт InfoObject по шаблону Documents\Document, прописывает Body=FileDesc(filePath),
// кладёт в указанный контейнер (или в контейнер задач, если containerId пуст),
// потом линкует в AttachedObjects текущей задачи.
private object DoAttachLocalFile( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 3 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var taskKey  = parts[0].Trim();
    var filePath = parts[1].Trim();
    var contStr  = parts.Length > 2 ? parts[2].Trim() : "";

    if( string.IsNullOrEmpty( taskKey ) || string.IsNullOrEmpty( filePath ) )
        return "ERROR:EmptyParams";
    if( !System.IO.File.Exists( filePath ) ) return "ERROR:FileNotFound";

    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    // Контейнер: либо выбранный, либо контейнер задач (для режима "Внутри задачи")
    DataContainer container = null;
    if( !string.IsNullOrEmpty( contStr ) )
    {
        uint cid;
        if( !uint.TryParse( contStr, out cid ) ) return "ERROR:InvalidContainerId";
        container = Service.GetDataContainer( cid );
        if( container == null ) return "ERROR:ContainerNotFound";
    }
    else
    {
        // Отдельный контейнер для inline-файлов с ключом All_Kanban_LocalFiles_Folder
        // (заводится администратором вручную на корне хранилища).
        container = Service.GetDataContainer( "All_Kanban_LocalFiles_Folder" );
        if( container == null ) return "ERROR:KanbanLocalFilesContainerNotFound";
    }

    var isInline = string.IsNullOrEmpty( contStr );

    try
    {
        if( GetAttachmentCount( task ) >= 50 ) return "ERROR:LimitReached";

        var fileName = System.IO.Path.GetFileName( filePath );
        var docName  = System.IO.Path.GetFileNameWithoutExtension( fileName );
        if( string.IsNullOrEmpty( docName ) ) docName = fileName;

        InfoObject mainDoc = null;

        if( isInline )
        {
            // Inline-режим: реальный однофазный шаблон "Простой документ (без версий)".
            // Файл прямо в Body, один Save, без виртуального диалога и без Machinery.
            var simpleTemplate = Service.GetTemplate( @"InfoObjects\BASIC\Documents\SimpleDocumentKanban" );
            if( simpleTemplate == null ) return "ERROR:SimpleDocTemplateNotFound";

            var sd = new InfoObject( container, simpleTemplate );
            // DocumentName храним с расширением - чтобы UI канбана мог определять
            // тип документа по расширению, а в дереве PLM было видно полное имя файла.
            try { sd["DocumentName"]         = fileName; } catch { }
            try { sd["FileName"]             = fileName; } catch { }
            try { sd["DoNotGeneratePreview"] = true; }     catch { }
            // Ключ задачи-владельца - для проверки доступа в PreCheckOperation
            // шаблона SimpleDocumentKanban: документ виден только тем, кто видит задачу.
            try { sd["OwnerTaskKey"]         = taskKey; }  catch { }
            try { sd.GetAttribute( "Body" ).Value.UploadFile( filePath ); }
            catch( Exception fex ) { return "ERROR:UploadFileFailed:" + fex.Message; }
            try { sd.IsBySystem = false; } catch { }

            using( Service.EnterNewGroupOperation() )
            {
                sd.Save();
                Service.SaveChanges();
            }
            mainDoc = sd;
        }
        else
        {
            // PLM-режим: Технический документ – двухфазная схема (главный + версия).
            // Требует лицензии PMSZ.PLMSoyuz.Machinery (есть на проде).
            var docTemplate = Service.GetTemplate( @"InfoObjects\STD\Documents\Document\TechnicalDocument" );
            if( docTemplate == null ) return "ERROR:DocTemplateNotFound";
            var verTemplate = Service.GetTemplate( @"InfoObjects\STD\Documents\DocumentVersion\TechnicalDocumentVersion" );
            if( verTemplate == null ) return "ERROR:VerTemplateNotFound";

            // 1. Главный объект – без ActualVersion (его выставим после persist версии)
            var td = new InfoObject( container, docTemplate );
            try { td["DocumentDesignation"] = docName; } catch { }
            try { td["ProductName"]         = docName; } catch { }
            try { td.IsBySystem = false; } catch { }
            using( Service.EnterNewGroupOperation() )
            {
                td.Save();
                Service.SaveChanges();
            }

            // 2. Версия – дочерняя к уже persisted td, файл в Body версии
            var ver = new InfoObject( td, verTemplate );
            try { ver["ProductName"]         = docName; } catch { }
            try { ver["DocumentDesignation"] = docName; } catch { }
            try { ver["DoNotGeneratePreview"] = true; } catch { }
            try { ver.GetAttribute( "Body" ).Value.UploadFile( filePath ); }
            catch( Exception fex ) { return "ERROR:UploadFileFailed:" + fex.Message; }
            try { ver.IsBySystem = false; } catch { }
            using( Service.EnterNewGroupOperation() )
            {
                ver.Save();
                Service.SaveChanges();
            }

            // 3. Проставляем ActualVersion после persist версии
            var editTd = td.GetEditable();
            editTd["ActualVersion"] = ver;
            using( Service.EnterNewGroupOperation() )
            {
                editTd.Save();
                Service.SaveChanges();
            }
            mainDoc = td;
        }

        // Линкуем созданный документ в AttachedObjects задачи
        var editTask = task.GetEditable();
        var attr     = editTask.GetAttribute( "AttachedObjects" );
        var set      = attr.LinkedInfoObjects.SafeToSet();
        set.Add( mainDoc );
        attr.LinkedInfoObjects = set;
        AppendAttachmentChangeLog( editTask, "Добавлен файл", fileName );
        using( Service.EnterNewGroupOperation() )
        {
            editTask.Save();
            Service.SaveChanges();
        }
        var key = string.IsNullOrEmpty( mainDoc.NameKey ) ? mainDoc.Id.ToString() : mainDoc.NameKey;
        return "OK|" + key;
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "Ошибка прикрепления файла" );
        return "ERROR:" + ex.Message;
    }
}

// ─── Вспомогательный поиск InfoObject по NameKey (для DoAddAttachment) ─
private InfoObject FindInfoObjectByKey( string key )
{
    if( string.IsNullOrEmpty( key ) ) return null;

    // Поиск по NameKey
    try
    {
        var searchOp = new SearchOperation( EntityIdentifier.InfoObject, "KbFindByKey" );
        var filter   = new SearchInfoObjectFilterItem();
        filter.PropertyId = SearchConditionFilterItem.SystemPropertyId.NameKey;
        filter.OperatorId = RelationalOperator.Equal;
        filter.Argument1  = key;
        searchOp.SearchExpressionTree = filter;
        searchOp.MaxSQLResultsCount   = 5;
        searchOp.MaxResultsCount      = 5;
        searchOp.Execute( false );

        if( searchOp.FoundObjects != null )
            foreach( var o in searchOp.FoundObjects.OfType<InfoObject>() )
                if( (string.IsNullOrEmpty( o.NameKey ) ? o.Id.ToString() : o.NameKey) == key ) return o;
    }
    catch { }

    // Если ключ числовой (Id объекта без NameKey) – поиск по Id
    long objId;
    if( long.TryParse( key, out objId ) )
    {
        try
        {
            var searchOp2 = new SearchOperation( EntityIdentifier.InfoObject, "KbFindById" );
            var filter2   = new SearchInfoObjectFilterItem();
            filter2.PropertyId = SearchConditionFilterItem.SystemPropertyId.Id;
            filter2.OperatorId = RelationalOperator.Equal;
            filter2.Argument1  = key;
            searchOp2.SearchExpressionTree = filter2;
            searchOp2.MaxSQLResultsCount   = 2;
            searchOp2.MaxResultsCount      = 2;
            searchOp2.Execute( false );

            if( searchOp2.FoundObjects != null )
                foreach( var o in searchOp2.FoundObjects.OfType<InfoObject>() )
                    if( o.Id.ToString() == key ) return o;
        }
        catch { }
    }

    return null;
}
// Комментарии (чат в карточке задачи)
// Парсинг JSON-массива комментариев
private System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>>
    ParseComments( string json )
{
    var result = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>>();
    if( string.IsNullOrEmpty( json ) || json.Trim().Length < 3 ) return result;

    int pos = 0;
    while( pos < json.Length )
    {
        int start = json.IndexOf( '{', pos );
        if( start < 0 ) break;
        int end = json.IndexOf( '}', start );
        if( end < 0 ) break;

        var obj = json.Substring( start + 1, end - start - 1 );
        var dict = new System.Collections.Generic.Dictionary<string, string>();

        int p = 0;
        while( p < obj.Length )
        {
            int kStart = obj.IndexOf( '"', p );
            if( kStart < 0 ) break;
            int kEnd = obj.IndexOf( '"', kStart + 1 );
            if( kEnd < 0 ) break;
            var key = obj.Substring( kStart + 1, kEnd - kStart - 1 );

            int colon = obj.IndexOf( ':', kEnd );
            if( colon < 0 ) break;

            int vStart = obj.IndexOf( '"', colon );
            if( vStart < 0 ) break;
            int vEnd = vStart + 1;
            while( vEnd < obj.Length )
            {
                if( obj[vEnd] == '"' && (vEnd == 0 || obj[vEnd - 1] != '\\') ) break;
                vEnd++;
            }
            var val = obj.Substring( vStart + 1, vEnd - vStart - 1 )
                         .Replace( "\\\\", "\x01" )
                         .Replace( "\\\"", "\"" )
                         .Replace( "\\n",  "\n"  )
                         .Replace( "\\r",  "\r"  )
                         .Replace( "\\t",  "\t"  )
                         .Replace( "\x01", "\\"  );
            dict[key] = val;
            p = vEnd + 1;
        }

        if( dict.Count > 0 ) result.Add( dict );
        pos = end + 1;
    }
    return result;
}

private string SerializeComments(
    System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>> items )
{
    var sb = new System.Text.StringBuilder();
    sb.Append( "[" );
    for( int i = 0; i < items.Count; i++ )
    {
        if( i > 0 ) sb.Append( "," );
        sb.Append( "{" );
        bool first = true;
        foreach( var kv in items[i] )
        {
            if( !first ) sb.Append( "," );
            sb.Append( "\"" + JsonEscape( kv.Key ) + "\":\"" + JsonEscape( kv.Value ) + "\"" );
            first = false;
        }
        sb.Append( "}" );
    }
    sb.Append( "]" );
    return sb.ToString();
}

private int GetCommentCount( InfoObject task )
{
    try
    {
        var json = task.GetString( "CommentsJSON" ) ?? "";
        if( json.Length < 3 ) return 0;
        return ParseComments( json ).Count;
    }
    catch { return 0; }
}

private string AppendCommentToTask(
    InfoObject task,
    User currentUser,
    string text,
    bool saveTask,
    out System.Collections.Generic.Dictionary<string, string> newItem )
{
    newItem = null;
    if( task == null ) return "ERROR:TaskNotFound";
    if( string.IsNullOrEmpty( text ) || text.Trim().Length == 0 ) return "ERROR:EmptyParams";

    text = text.Trim();
    if( text.Length > 2000 ) text = text.Substring( 0, 2000 );
    text = NormalizeCommentText( text );

    var userKey  = currentUser != null
                 ? (string.IsNullOrEmpty( currentUser.NameKey ) ? currentUser.AccountId : currentUser.NameKey)
                 : "";
    var userName = currentUser != null ? currentUser.ToString() : "";

    var json  = task.GetString( "CommentsJSON" ) ?? "";
    var items = ParseComments( json );
    if( items.Count >= 200 ) return "ERROR:LimitReached";

    newItem = new System.Collections.Generic.Dictionary<string, string>();
    newItem["text"]       = text;
    newItem["author"]     = userKey;
    newItem["authorName"] = userName;
    newItem["date"]       = DateTime.Now.ToString( "dd.MM.yyyy HH:mm" );
    items.Add( newItem );
    var newIndex = (items.Count - 1).ToString();

    task["CommentsJSON"] = SerializeComments( items );
    newItem["index"] = newIndex;
    if( saveTask ) task.Save();

    try { SendCommentNotification( task, currentUser, text ); } catch { }
    return null;
}

// AddComment
private object DoAddComment( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var taskKey = parts[0].Trim();
    var text    = parts[1].Trim();

    if( string.IsNullOrEmpty( taskKey ) || string.IsNullOrEmpty( text ) )
        return "ERROR:EmptyParams";

    if( text.Length > 2000 ) text = text.Substring( 0, 2000 );
    text = NormalizeCommentText( text );

    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    var currentUser = Service.GetCurrentUser();
    System.Collections.Generic.Dictionary<string, string> newItem;
    var err = AppendCommentToTask( task, currentUser, text, true, out newItem );
    if( !string.IsNullOrEmpty( err ) ) return err;

    var userName = "";
    try { newItem.TryGetValue( "authorName", out userName ); } catch { }
    var initials = GetInitials( userName );
    return "{\"text\":\"" + JsonEscape( newItem["text"] ) + "\","
         + "\"author\":\"" + JsonEscape( newItem["author"] ) + "\","
         + "\"authorName\":\"" + JsonEscape( userName ) + "\","
         + "\"initials\":\"" + JsonEscape( initials ) + "\","
         + "\"date\":\"" + JsonEscape( newItem["date"] ) + "\","
         + "\"editedAt\":\"\","
         + "\"isMine\":true,"
         + "\"index\":" + newItem["index"] + "}";
}

// GetComments
private object DoGetComments( object inputParams )
{
    var taskKey = GetParamStr( inputParams );
    if( string.IsNullOrEmpty( taskKey ) ) return "[]";

    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "[]";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "[]";

    var json  = task.GetString( "CommentsJSON" ) ?? "";
    var items = ParseComments( json );
    if( items.Count == 0 ) return "[]";

    var currentUser = Service.GetCurrentUser();
    var myKey = currentUser != null
              ? (string.IsNullOrEmpty( currentUser.NameKey ) ? currentUser.AccountId : currentUser.NameKey)
              : "";

    var sb = new System.Text.StringBuilder();
    sb.Append( "[" );
    for( int i = 0; i < items.Count; i++ )
    {
        if( i > 0 ) sb.Append( "," );

        string aText = "", aAuthor = "", aAuthorName = "", aDate = "", aEdited = "";
        items[i].TryGetValue( "text", out aText );
        items[i].TryGetValue( "author", out aAuthor );
        items[i].TryGetValue( "authorName", out aAuthorName );
        items[i].TryGetValue( "date", out aDate );
        items[i].TryGetValue( "editedAt", out aEdited );

        // Обратная совместимость: legacy-записи прошли через HtmlEnc и
        // содержат &quot; / &lt; / &gt; / &#39; / &amp;. Декодируем при
        // чтении – клиент ВСЁ РАВНО заново экранирует на рендере.
        aText = DecodeLegacyHtmlEnc( aText ?? "" );

        var initials = GetInitials( aAuthorName ?? "" );
        var isMine   = aAuthor == myKey;

        sb.Append( "{\"text\":\"" + JsonEscape( aText ?? "" ) + "\","
                 + "\"author\":\"" + JsonEscape( aAuthor ?? "" ) + "\","
                 + "\"authorName\":\"" + JsonEscape( aAuthorName ?? "" ) + "\","
                 + "\"initials\":\"" + JsonEscape( initials ) + "\","
                 + "\"date\":\"" + JsonEscape( aDate ?? "" ) + "\","
                 + "\"editedAt\":\"" + JsonEscape( aEdited ?? "" ) + "\","
                 + "\"isMine\":" + (isMine ? "true" : "false") + ","
                 + "\"index\":" + i + "}" );
    }
    sb.Append( "]" );
    return sb.ToString();
}

// DeleteComment
private object DoDeleteComment( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var taskKey = parts[0].Trim();
    int index;
    if( !int.TryParse( parts[1].Trim(), out index ) ) return "ERROR:BadIndex";

    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    var json  = task.GetString( "CommentsJSON" ) ?? "";
    var items = ParseComments( json );

    if( index < 0 || index >= items.Count ) return "ERROR:IndexOutOfRange";

    var currentUser = Service.GetCurrentUser();
    var myKey = currentUser != null
              ? (string.IsNullOrEmpty( currentUser.NameKey ) ? currentUser.AccountId : currentUser.NameKey)
              : "";
    string commentAuthor = "";
    items[index].TryGetValue( "author", out commentAuthor );

    if( commentAuthor != myKey ) return "ERROR:NotOwner";

    items.RemoveAt( index );

    task["CommentsJSON"] = items.Count > 0 ? SerializeComments( items ) : "";
    task.Save();
    return "OK";
}

// EditComment
// inputParams: "taskKey|index|newText"
// Редактирование разрешено только автору комментария.
// При успехе ставит editedAt = DateTime.Now.
private object DoEditComment( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 3 );
    if( parts.Length < 3 ) return "ERROR:BadFormat";

    var taskKey = parts[0].Trim();
    int index;
    if( !int.TryParse( parts[1].Trim(), out index ) ) return "ERROR:BadIndex";

    // ВНИМАНИЕ: текст НЕ Trim() – пользователь мог намеренно оставить
    // переносы / пробелы в начале и конце.
    var newText = parts[2];
    if( string.IsNullOrEmpty( newText ) || newText.Trim().Length == 0 )
        return "ERROR:EmptyText";

    if( newText.Length > 2000 ) newText = newText.Substring( 0, 2000 );
    newText = NormalizeCommentText( newText );

    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    var json  = task.GetString( "CommentsJSON" ) ?? "";
    var items = ParseComments( json );
    if( index < 0 || index >= items.Count ) return "ERROR:IndexOutOfRange";

    var currentUser = Service.GetCurrentUser();
    var myKey = currentUser != null
              ? (string.IsNullOrEmpty( currentUser.NameKey ) ? currentUser.AccountId : currentUser.NameKey)
              : "";
    string commentAuthor = "";
    items[index].TryGetValue( "author", out commentAuthor );
    if( commentAuthor != myKey ) return "ERROR:NotOwner";

    items[index]["text"]     = newText;
    items[index]["editedAt"] = DateTime.Now.ToString( "dd.MM.yyyy HH:mm" );

    task["CommentsJSON"] = SerializeComments( items );
    task.Save();

    return "{\"text\":\""    + JsonEscape( newText )                  + "\","
         + "\"editedAt\":\"" + JsonEscape( items[index]["editedAt"] ) + "\","
         + "\"index\":"      + index + "}";
}

// GetCardCommentCount
// Лёгкий метод для синхронизации бейджа карточки на доске после
// AddComment / DeleteComment без полного RefreshBoard.
private object DoGetCardCommentCount( object inputParams )
{
    var nameKey = GetParamStr( inputParams );
    var task    = GetTaskByKeyOrNull( nameKey );
    if( task == null ) return "0";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "0";
    return GetCommentCount( task ).ToString();
}
// Подзадачи / чекбоксы (Phase 2 – задача №2)
// Хранение: текстовый атрибут SubtasksJSON (тот же формат, что CommentsJSON).
// Денормализация: счётчики SubtasksTotal / SubtasksDone (IntegerNumber)
// обновляются внутри каждой мутирующей операции, чтобы BeforeRender
// не парсил JSON для каждой карточки.

private System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>>
    ParseSubtasks( string json )
{
    return ParseComments( json );
}

private string SerializeSubtasks(
    System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>> items )
{
    return SerializeComments( items );
}

// AddSubtask
// inputParams: "taskKey|text"
// Возвращает JSON созданной подзадачи или ERROR:*
private object DoAddSubtask( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var taskKey = parts[0].Trim();
    var text    = parts[1].Trim();
    if( string.IsNullOrEmpty( taskKey ) ) return "ERROR:NoKey";
    if( string.IsNullOrEmpty( text ) )    return "ERROR:EmptyText";
    if( text.Length > 500 ) text = text.Substring( 0, 500 );

    try
    {
        var task = GetTaskByKeyOrNull( taskKey );
        if( task == null ) return "ERROR:NotFound";
        if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

        // Чтение оригинальной оболочки – вне транзакции.
        var json  = task.GetString( "SubtasksJSON" ) ?? "";
        var items = ParseSubtasks( json );

        if( items.Count >= 200 ) return "ERROR:LimitReached";

        // Генерация id: s + (max(id)+1)
        int maxId = 0;
        for( int i = 0; i < items.Count; i++ )
        {
            string v;
            if( items[i].TryGetValue( "id", out v ) && v.StartsWith( "s" ) )
            {
                int n;
                if( int.TryParse( v.Substring( 1 ), out n ) && n > maxId ) maxId = n;
            }
        }
        var newId = "s" + (maxId + 1).ToString();

        var user = Service.GetCurrentUser();
        var userName = user != null ? (user.ToString() ?? "") : "";
        var now = DateTime.Now.ToString( "yyyy-MM-ddTHH:mm:ss",
                       System.Globalization.CultureInfo.InvariantCulture );

        var item = new System.Collections.Generic.Dictionary<string, string>();
        item["id"]        = newId;
        item["text"]      = text;
        item["done"]      = "0";
        item["createdBy"] = userName;
        item["createdAt"] = now;
        items.Add( item );

        // Денормализованные счётчики
        int total = items.Count;
        int done  = 0;
        for( int i = 0; i < items.Count; i++ )
        {
            string v;
            if( items[i].TryGetValue( "done", out v ) && v == "1" ) done++;
        }

        task["SubtasksJSON"]  = SerializeSubtasks( items );
        task["SubtasksTotal"] = total;
        task["SubtasksDone"]  = done;
        AppendSubtaskChangeLog( task, "Добавлен пункт", text );
        task.Save();

        var sb = new System.Text.StringBuilder();
        sb.Append( "{" );
        bool first = true;
        foreach( var kv in item )
        {
            if( !first ) sb.Append( "," );
            sb.Append( "\"" + JsonEscape( kv.Key ) + "\":\"" + JsonEscape( kv.Value ) + "\"" );
            first = false;
        }
        sb.Append( "}" );
        return sb.ToString();
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "KanbanScreen.DoAddSubtask: " + ex.Message );
        return "ERROR:Internal";
    }
}

// ToggleSubtask
// inputParams: "taskKey|subtaskId"
// Переворачивает done 0↔1, обновляет doneBy/doneAt.
private object DoToggleSubtask( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var taskKey   = parts[0].Trim();
    var subtaskId = parts[1].Trim();

    try
    {
        var task = GetTaskByKeyOrNull( taskKey );
        if( task == null ) return "ERROR:NotFound";
        if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

        var items = ParseSubtasks( task.GetString( "SubtasksJSON" ) ?? "" );

        int idx = -1;
        for( int i = 0; i < items.Count; i++ )
        {
            string v;
            if( items[i].TryGetValue( "id", out v ) && v == subtaskId ) { idx = i; break; }
        }
        if( idx < 0 ) return "ERROR:NoSuchSubtask";

        string oldDone;
        items[idx].TryGetValue( "done", out oldDone );
        bool newDone = !(oldDone == "1");
        items[idx]["done"] = newDone ? "1" : "0";

        // Получаем текст подзадачи для лога
        string subText = "";
        items[idx].TryGetValue( "text", out subText );
        string actionStr = newDone ? "Выполнено" : "Снята отметка";

        if( newDone )
        {
            var user = Service.GetCurrentUser();
            items[idx]["doneBy"] = user != null ? (user.ToString() ?? "") : "";
            items[idx]["doneAt"] = DateTime.Now.ToString( "yyyy-MM-ddTHH:mm:ss",
                                       System.Globalization.CultureInfo.InvariantCulture );
        }
        else
        {
            items[idx].Remove( "doneBy" );
            items[idx].Remove( "doneAt" );
        }

        int total = items.Count;
        int done  = 0;
        for( int i = 0; i < items.Count; i++ )
        {
            string v;
            if( items[i].TryGetValue( "done", out v ) && v == "1" ) done++;
        }

        task["SubtasksJSON"]  = SerializeSubtasks( items );
        task["SubtasksTotal"] = total;
        task["SubtasksDone"]  = done;
        AppendSubtaskChangeLog( task, actionStr, subText );
        task.Save();

        var sb = new System.Text.StringBuilder();
        sb.Append( "{" );
        bool first = true;
        foreach( var kv in items[idx] )
        {
            if( !first ) sb.Append( "," );
            sb.Append( "\"" + JsonEscape( kv.Key ) + "\":\"" + JsonEscape( kv.Value ) + "\"" );
            first = false;
        }
        sb.Append( "}" );
        return sb.ToString();
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "KanbanScreen.DoToggleSubtask: " + ex.Message );
        return "ERROR:Internal";
    }
}

// EditSubtask
// inputParams: "taskKey|subtaskId|newText"
// Меняет text, проставляет editedBy/editedAt. Возвращает JSON обновлённого пункта.
private object DoEditSubtask( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 3 );
    if( parts.Length < 3 ) return "ERROR:BadFormat";

    var taskKey   = parts[0].Trim();
    var subtaskId = parts[1].Trim();
    var newText   = parts[2].Trim();

    if( string.IsNullOrEmpty( taskKey ) )   return "ERROR:NoKey";
    if( string.IsNullOrEmpty( subtaskId ) ) return "ERROR:NoId";
    if( string.IsNullOrEmpty( newText ) )   return "ERROR:EmptyText";
    if( newText.Length > 500 ) newText = newText.Substring( 0, 500 );

    try
    {
        var task = GetTaskByKeyOrNull( taskKey );
        if( task == null ) return "ERROR:NotFound";
        if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

        var items = ParseSubtasks( task.GetString( "SubtasksJSON" ) ?? "" );

        int idx = -1;
        for( int i = 0; i < items.Count; i++ )
        {
            string v;
            if( items[i].TryGetValue( "id", out v ) && v == subtaskId ) { idx = i; break; }
        }
        if( idx < 0 ) return "ERROR:NoSuchSubtask";

        string oldText = "";
        items[idx].TryGetValue( "text", out oldText );
        if( oldText == newText )
        {
            var sbNoop = new System.Text.StringBuilder();
            sbNoop.Append( "{" );
            bool firstNoop = true;
            foreach( var kv in items[idx] )
            {
                if( !firstNoop ) sbNoop.Append( "," );
                sbNoop.Append( "\"" + JsonEscape( kv.Key ) + "\":\"" + JsonEscape( kv.Value ) + "\"" );
                firstNoop = false;
            }
            sbNoop.Append( "}" );
            return sbNoop.ToString();
        }

        items[idx]["text"] = newText;

        var user = Service.GetCurrentUser();
        items[idx]["editedBy"] = user != null ? (user.ToString() ?? "") : "";
        items[idx]["editedAt"] = DateTime.Now.ToString( "yyyy-MM-ddTHH:mm:ss",
                                     System.Globalization.CultureInfo.InvariantCulture );

        task["SubtasksJSON"] = SerializeSubtasks( items );
        // SubtasksTotal/SubtasksDone не меняются при редактировании текста
        AppendSubtaskChangeLog( task, "Изменён пункт", oldText + " → " + newText );
        task.Save();

        var sb = new System.Text.StringBuilder();
        sb.Append( "{" );
        bool first = true;
        foreach( var kv in items[idx] )
        {
            if( !first ) sb.Append( "," );
            sb.Append( "\"" + JsonEscape( kv.Key ) + "\":\"" + JsonEscape( kv.Value ) + "\"" );
            first = false;
        }
        sb.Append( "}" );
        return sb.ToString();
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "KanbanScreen.DoEditSubtask: " + ex.Message );
        return "ERROR:Internal";
    }
}

// ReorderSubtasks
// inputParams: "taskKey|id1,id2,id3,..."
// Принимает новый порядок ID и переупорядочивает массив. Возвращает "OK" или ERROR.
// Если переданный набор ID не совпадает с текущим – возвращает ERROR:Mismatch.
private object DoReorderSubtasks( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var taskKey  = parts[0].Trim();
    var orderStr = parts[1].Trim();
    if( string.IsNullOrEmpty( taskKey ) )  return "ERROR:NoKey";
    if( string.IsNullOrEmpty( orderStr ) ) return "ERROR:EmptyOrder";

    var newOrder = orderStr.Split( new char[]{ ',' }, StringSplitOptions.RemoveEmptyEntries );
    if( newOrder.Length == 0 ) return "ERROR:EmptyOrder";

    try
    {
        var task = GetTaskByKeyOrNull( taskKey );
        if( task == null ) return "ERROR:NotFound";
        if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

        var items = ParseSubtasks( task.GetString( "SubtasksJSON" ) ?? "" );
        if( items.Count != newOrder.Length ) return "ERROR:Mismatch";

        var byId = new System.Collections.Generic.Dictionary<string,
                       System.Collections.Generic.Dictionary<string, string>>( items.Count );
        for( int i = 0; i < items.Count; i++ )
        {
            string v;
            if( !items[i].TryGetValue( "id", out v ) || string.IsNullOrEmpty( v ) ) return "ERROR:CorruptItem";
            if( byId.ContainsKey( v ) ) return "ERROR:DuplicateId";
            byId[v] = items[i];
        }

        var reordered = new System.Collections.Generic.List<
            System.Collections.Generic.Dictionary<string, string>>( items.Count );
        for( int i = 0; i < newOrder.Length; i++ )
        {
            var id = newOrder[i].Trim();
            if( !byId.ContainsKey( id ) ) return "ERROR:UnknownId";
            reordered.Add( byId[id] );
        }

        task["SubtasksJSON"] = SerializeSubtasks( reordered );
        // Total/Done не меняются; лог перестановки не пишем намеренно
        task.Save();
        return "OK";
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "KanbanScreen.DoReorderSubtasks: " + ex.Message );
        return "ERROR:Internal";
    }
}

// DeleteSubtask
// inputParams: "taskKey|subtaskId"
private object DoDeleteSubtask( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var taskKey   = parts[0].Trim();
    var subtaskId = parts[1].Trim();

    try
    {
        var task = GetTaskByKeyOrNull( taskKey );
        if( task == null ) return "ERROR:NotFound";
        if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

        var items = ParseSubtasks( task.GetString( "SubtasksJSON" ) ?? "" );

        int idx = -1;
        for( int i = 0; i < items.Count; i++ )
        {
            string v;
            if( items[i].TryGetValue( "id", out v ) && v == subtaskId ) { idx = i; break; }
        }
        if( idx < 0 ) return "ERROR:NoSuchSubtask";

        // Получаем текст перед удалением для лога
        string subText = "";
        items[idx].TryGetValue( "text", out subText );

        items.RemoveAt( idx );

        int total = items.Count;
        int done  = 0;
        for( int i = 0; i < items.Count; i++ )
        {
            string v;
            if( items[i].TryGetValue( "done", out v ) && v == "1" ) done++;
        }

        task["SubtasksJSON"]  = items.Count > 0 ? SerializeSubtasks( items ) : "";
        task["SubtasksTotal"] = total;
        task["SubtasksDone"]  = done;
        AppendSubtaskChangeLog( task, "Удалён пункт", subText );
        task.Save();

        return "OK";
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "KanbanScreen.DoDeleteSubtask: " + ex.Message );
        return "ERROR:Internal";
    }
}

// GetSubtasks
// inputParams: "taskKey" – возвращает сырой JSON-массив подзадач.
private object DoGetSubtasks( object inputParams )
{
    var taskKey = GetParamStr( inputParams ).Trim();
    if( string.IsNullOrEmpty( taskKey ) ) return "[]";
    try
    {
        var task = GetTaskByKeyOrNull( taskKey );
        if( task == null ) return "[]";
        if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "[]";
        var json = task.GetString( "SubtasksJSON" ) ?? "";
        return string.IsNullOrEmpty( json ) ? "[]" : json;
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "KanbanScreen.DoGetSubtasks: " + ex.Message );
        return "[]";
    }
}

// Уведомление о новом комментарии (Exclamation)
private const string COMMENT_MSG_TEMPLATE_PATH = @"WorkLoads\BASIC\Message\ExclamationKanban";

private void SendCommentNotification( InfoObject task, User author, string text )
{
    if( task == null || author == null ) return;

    Template tmpl = Service.GetTemplate( COMMENT_MSG_TEMPLATE_PATH );
    if( tmpl == null ) return;

    User assignee = null;
    try { assignee = task.GetUser( "Assignee" ); } catch { }

    var creatorKey = task.GetString( "Creator" ) ?? "";
    User creator = null;
    if( !string.IsNullOrEmpty( creatorKey ) )
        creator = TryFindUserByKey( creatorKey );

    var recipients = new System.Collections.Generic.List<User>();

    if( assignee != null && assignee.Id != author.Id )
        recipients.Add( assignee );

    if( creator != null && creator.Id != author.Id
        && ( assignee == null || creator.Id != assignee.Id ) )
        recipients.Add( creator );

    if( recipients.Count == 0 ) return;

    var taskName = task.GetString( "TaskName" ) ?? task.ToString();
    var preview  = text.Length > 300 ? text.Substring( 0, 300 ) + "…" : text;
    var subject  = "Новый комментарий в задаче «" + taskName + "»: " + preview;

    foreach( var u in recipients )
        SendWorkItemNotify( tmpl, u, subject, preview, task.NameKey );
}

private User TryFindUserByKey( string key )
{
    if( string.IsNullOrEmpty( key ) ) return null;
    try
    {
        foreach( var u in Service.AllUsers )
        {
            if( u == null || u.IsGroup ) continue;
            var k = string.IsNullOrEmpty( u.NameKey ) ? u.AccountId : u.NameKey;
            if( k == key ) return u;
        }
    }
    catch { }
    return null;
}

private void SendWorkItemNotify( Template tmpl, User recipient, string subject, string details, string taskKey )
{
    try
    {
        var w = new WorkItem( tmpl, recipient );
        w[ "Subject" ] = subject;
        try { w[ "TaskDetails" ] = details; } catch { }
        try { w[ "SilentMode"  ] = true;    } catch { }
        w.Params     = taskKey;
        w.IsBySystem = true;
        w.Send();
        // НЕ пре-маркируем viewed – запись должна появиться в папке оповещений как "Новая".
        // Сброс бейджа – после показа custom popup в ExclamationKanban.OnUpdated.
    }
    catch( Exception ex )
    {
        Service.WriteToServerLog( "KanbanNotify", "Error sending comment notification: " + ex.Message );
    }
}

// JSON-экранирование строки
private string JsonEscape( string s )
{
    if( string.IsNullOrEmpty( s ) ) return "";
    return s.Replace( "\\", "\\\\" )
            .Replace( "\"", "\\\"" )
            .Replace( "\n", "\\n"  )
            .Replace( "\r", "\\r"  )
            .Replace( "\t", "\\t"  );
}

// Нейтрализация текста комментария
// Используется в DoAddComment / DoEditComment ВМЕСТО HtmlEnc, чтобы
// не плодить «&quot;» в уже сохранённом тексте: клиент всё равно
// экранирует через tcmChatEsc на рендере. Здесь только подменяем
// '<' и '>' на типографические аналоги – это блокирует XSS-вектор
// и сохраняет сырые кавычки/амперсанды.
private string NormalizeCommentText( string s )
{
    if( string.IsNullOrEmpty( s ) ) return "";
    return s.Replace( "<", "‹" ).Replace( ">", "›" );
}

// Декодирование legacy-комментариев
// Используется только при ЧТЕНИИ (DoGetComments). В БД ничего не
// перезаписываем. Обратное преобразование того, что делал старый
// HtmlEnc-путь.
private string DecodeLegacyHtmlEnc( string s )
{
    if( string.IsNullOrEmpty( s ) ) return "";
    return s.Replace( "&quot;", "\"" )
            .Replace( "&#39;",  "'"  )
            .Replace( "&lt;",   "‹"  )
            .Replace( "&gt;",   "›"  )
            .Replace( "&amp;",  "&"  );
}

// Обрезает описание до ~120 символов по границе слова
private string TruncDesc( string s )
{
    if( string.IsNullOrEmpty( s ) || s.Length <= 120 ) return s;
    var cut = s.LastIndexOf( ' ', 119 );
    return ( cut > 30 ? s.Substring( 0, cut ) : s.Substring( 0, 120 ) ) + "…";
}

// Запись в ChangeLog для вложений
private void AppendAttachmentChangeLog( InfoObject task, string action, string itemName )
{
    try
    {
        string authorName = "";
        try { authorName = Service.GetCurrentUser()?.ToString() ?? ""; } catch { }

        var entry = "{\"d\":\"" + DateTime.Now.ToString( "dd.MM.yyyy HH:mm" )
                  + "\",\"a\":\"" + JsonEscape( authorName )
                  + "\",\"c\":[{\"f\":\"Вложение\",\"o\":\"" + JsonEscape( action )
                  + "\",\"n\":\"" + JsonEscape( itemName ) + "\"}]}";

        var oldLog = task.GetString( "ChangeLog" ) ?? "";
        string newLog;
        if( string.IsNullOrEmpty( oldLog ) || oldLog.Trim() == "[]" )
            newLog = "[" + entry + "]";
        else
            newLog = oldLog.TrimEnd().TrimEnd( ']' ) + "," + entry + "]";

        task["ChangeLog"] = newLog;
    }
    catch { /* ChangeLog не критичен */ }
}

// Запись в ChangeLog для подзадач (чек-листа)
private void AppendSubtaskChangeLog( InfoObject task, string action, string subtaskText )
{
    if( task == null ) return;

    string authorName = "";
    try { var usr = Service.GetCurrentUser(); if( usr != null ) authorName = usr.ToString() ?? ""; }
    catch { }

    var preview = subtaskText ?? "";
    if( preview.Length > 80 ) preview = preview.Substring( 0, 80 ) + "…";

    var entry = "{\"d\":\"" + DateTime.Now.ToString( "dd.MM.yyyy HH:mm" )
              + "\",\"a\":\"" + JsonEscape( authorName )
              + "\",\"c\":[{\"f\":\"Чек-лист\",\"o\":\"" + JsonEscape( action )
              + "\",\"n\":\"" + JsonEscape( preview ) + "\"}]}";

    var oldLog = task.GetString( "ChangeLog" ) ?? "";
    string newLog;
    if( string.IsNullOrEmpty( oldLog ) || oldLog.Trim() == "[]" )
        newLog = "[" + entry + "]";
    else
        newLog = oldLog.TrimEnd().TrimEnd( ']' ) + "," + entry + "]";

    task["ChangeLog"] = newLog;
}
// Экспорт текущей доски в Excel
// inputParams: игнорируется. Все фильтры читаются из obj.PropertyBag
// (KbViewMode, KbPeriodFrom, KbPeriodTo) – те же значения, что использует
// BeforeRender. Excel COM открывается на машине пользователя (Visible=true).
private object DoExportToExcel( InfoObject obj, object inputParams )
{
    var container = Service.GetDataContainer( "All_Kanban_Tasks_Folder" );
    if( container == null ) return "ERROR:ContainerNotFound";

    var currentUser = Service.GetCurrentUser();
    var role        = GetUserRole( currentUser );

    var viewMode = (obj.PropertyBag["KbViewMode"] as string) ?? "my";

    var allowedIds = GetAllowedUserIdSet( currentUser, role, viewMode );

    string myCreatorKey = "";
    if( viewMode == "myCreated" && currentUser != null )
    {
        myCreatorKey = !string.IsNullOrEmpty( currentUser.NameKey ) ? currentUser.NameKey
                     : !string.IsNullOrEmpty( currentUser.AccountId ) ? currentUser.AccountId
                     : currentUser.Id.ToString();
    }

    DateTime? periodFrom = null, periodTo = null;
    var sFrom = obj.PropertyBag["KbPeriodFrom"] as string;
    var sTo   = obj.PropertyBag["KbPeriodTo"]   as string;
    if( !string.IsNullOrEmpty( sFrom ) )
    {
        DateTime tmp;
        if( DateTime.TryParseExact( sFrom, "dd.MM.yyyy",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out tmp ) )
            periodFrom = tmp;
    }
    if( !string.IsNullOrEmpty( sTo ) )
    {
        DateTime tmp;
        if( DateTime.TryParseExact( sTo, "dd.MM.yyyy",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out tmp ) )
            periodTo = tmp;
    }

    var raw4 = new System.Collections.Generic.List<InfoObject>[4];
    for( int i = 0; i < 4; i++ )
        raw4[i] = new System.Collections.Generic.List<InfoObject>();

    foreach( var task in container.RootInfoObjects )
    {
        var assignee = task.GetUser( "Assignee" );
        if( assignee == null ) continue;

        if( allowedIds != null && !allowedIds.Contains( assignee.Id.ToString() ) )
            continue;
        if( IsTaskPrivate( task ) ) continue;

        if( viewMode == "myCreated" )
        {
            var taskCreator = "";
            try { taskCreator = task.GetString( "Creator" ) ?? ""; } catch { }
            if( taskCreator != myCreatorKey ) continue;
            if( assignee.Id.ToString() == currentUser.Id.ToString() ) continue;
        }

        int status = GetStatusIndex( task );
        if( status < 0 || status > 3 ) status = 0;

        if( !IsTaskInPeriod( task, status, periodFrom, periodTo ) ) continue;

        raw4[status].Add( task );
    }

    for( int i = 0; i < STATUS_DONE; i++ )
        raw4[i].Sort( CompareActiveTasks );

    raw4[STATUS_DONE].Sort( (a, b) => {
        DateTime da = default(DateTime), db = default(DateTime);
        try { da = a.GetValue<DateTime>( "CompletedDate" ); } catch {}
        try { db = b.GetValue<DateTime>( "CompletedDate" ); } catch {}
        return db.CompareTo( da );
    });

    int total = raw4[0].Count + raw4[1].Count + raw4[2].Count + raw4[3].Count;
    if( total == 0 ) return "EMPTY";

    string[][] cols = new string[][] {
        new[] { "Колонка",         "@" },
        new[] { "Название",        "@" },
        new[] { "Описание",        "@" },
        new[] { "Исполнитель",     "@" },
        new[] { "Поручил",         "@" },
        new[] { "Приоритет",       "@" },
        new[] { "Срок",            "@" },
        new[] { "Дата создания",   "@" },
        new[] { "Дата завершения", "@" },
        new[] { "Просрочена",      "@" },
        new[] { "Теги",            "@" },
        new[] { "Чек-лист",        "@" },
        new[] { "Комментариев",    "0" },
        new[] { "Вложений",        "0" }
    };
    string[] colNames = new[] {
        "Надо сделать", "В работе", "Ожидание", "Готово"
    };

    KbExcel doc = null;
    try
    {
        doc = new KbExcel();
        doc.CreateBook();
        doc.OpenWorksheet( 1 );

        int rowCount = 1;
        string lastColumnKey = "A";
        for( int c = 0; c < cols.Length; c++ )
        {
            var colKey = KbExcel.GetColNameFromIndex( c + 1 );
            lastColumnKey = colKey;
            var addr = colKey + rowCount;
            doc.AlignHorizontal( addr, KbXlAlignHorizontal.xlCenter );
            doc.AlignVertical(   addr, KbXlAlignVertical.xlCenter );
            doc.SetValue( addr, cols[c][0], "@" );
        }
        var hdrRange = "A1:" + lastColumnKey + "1";
        doc.SetFont( hdrRange, new System.Drawing.Font( "Calibri", 11, System.Drawing.FontStyle.Bold ) );
        doc.SetColor( hdrRange, 15, 1 );
        doc.SetRangeBorders( hdrRange, KbXlLineStyle.xlContinuous, KbXlBorderWeight.xlThin );

        for( int statusIdx = 0; statusIdx < 4; statusIdx++ )
        {
            foreach( var task in raw4[statusIdx] )
            {
                rowCount++;
                var rowVals = ExportBuildRow( task, statusIdx, colNames );
                for( int c = 0; c < cols.Length; c++ )
                {
                    var addr = KbExcel.GetColNameFromIndex( c + 1 ) + rowCount;
                    doc.SetValue( addr, rowVals[c] ?? "", cols[c][1] );
                }
                doc.AlignHorizontal( "A" + rowCount + ":" + lastColumnKey + rowCount, KbXlAlignHorizontal.xlLeft );
                doc.AlignVertical(   "A" + rowCount + ":" + lastColumnKey + rowCount, KbXlAlignVertical.xlTop  );

                if( rowVals[9] == "да" )
                    doc.SetColor( "A" + rowCount + ":" + lastColumnKey + rowCount, 38, 1 );
            }
        }

        var allRange = "A1:" + lastColumnKey + rowCount;
        doc.SetRangeBorders( allRange, KbXlLineStyle.xlContinuous, KbXlBorderWeight.xlThin );
        doc.BorderAround(    allRange, KbXlLineStyle.xlContinuous, KbXlBorderWeight.xlMedium );
        doc.SetWrapText( "B2:C" + rowCount );
        doc.AutoFit( allRange );

        doc.Visible = true;
        doc.BringToFront();
    }
    catch( ServerOperationFailedException e )
    {
        var inner = e.InnerException;
        if( inner != null ) Service.HandleException( inner );
        else                Service.HandleException( e );
        return "ERROR:" + e.Message;
    }
    catch( System.Reflection.TargetInvocationException e )
    {
        var inner = e.InnerException as System.Runtime.InteropServices.COMException;
        if( inner == null || inner.ErrorCode != -2146827284 )
            Service.UI.ShowMessage( "Не удалось сформировать Excel-файл. Excel установлен?", e );
        return "ERROR:" + e.Message;
    }
    catch( Exception e )
    {
        Service.UI.ShowMessage( "Не удалось сформировать Excel-файл. Обратитесь к администратору.", e );
        return "ERROR:" + e.Message;
    }
    finally
    {
        if( doc != null && doc.Visible == false )
        {
            try { doc.CloseBook(); } catch { }
            try { doc.Quit();      } catch { }
            try { doc.Kill();      } catch { }
        }
    }
    return "OK";
}

// Строит массив значений для одной строки Excel.
// Порядок строго соответствует массиву cols в DoExportToExcel.
private string[] ExportBuildRow( InfoObject task, int statusIdx, string[] colNames )
{
    string title          = "";
    string details        = "";
    string assigneeName   = "";
    string creatorName    = "";
    string priorityKey    = "medium";
    string dueDateStr     = "";
    string createdStr     = "";
    string completedStr   = "";
    string overdueStr     = "нет";
    string tagsStr        = "";
    string subtasksStr    = "";
    int    commentCnt     = 0;
    int    attachCnt      = 0;

    try { title   = task.GetString( "TaskName"    ) ?? ""; } catch { }
    try { details = task.GetString( "TaskDetails" ) ?? ""; } catch { }
    try { tagsStr = task.GetString( "Tags"        ) ?? ""; } catch { }

    try
    {
        var u = task.GetUser( "Assignee" );
        if( u != null ) assigneeName = u.ToString() ?? "";
    }
    catch { }

    try
    {
        var creatorKey = task.GetString( "Creator" ) ?? "";
        if( !string.IsNullOrEmpty( creatorKey ) )
        {
            var u = FindUserByKeyOrNull( creatorKey );
            if( u != null ) creatorName = u.ToString() ?? "";
            else            creatorName = creatorKey;
        }
    }
    catch { }

    try
    {
        var nv = task.GetNamedValue( "Priority" );
        if( nv != null ) priorityKey = nv.GetValue<string>() ?? "medium";
    }
    catch { }

    try
    {
        var dd = task.GetValue<DateTime>( "DueDate" );
        if( dd != default(DateTime) ) dueDateStr = dd.ToString( "dd.MM.yyyy" );
    }
    catch { }
    try
    {
        if( task.DateCreated != default(DateTime) )
            createdStr = task.DateCreated.ToString( "dd.MM.yyyy" );
    }
    catch { }
    try
    {
        var cd = task.GetValue<DateTime>( "CompletedDate" );
        if( cd != default(DateTime) ) completedStr = cd.ToString( "dd.MM.yyyy" );
    }
    catch { }

    try
    {
        var dd = task.GetValue<DateTime>( "DueDate" );
        if( dd != default(DateTime) && dd.Date < DateTime.Today && statusIdx != 3 )
            overdueStr = "да";
    }
    catch { }

    int subTotal = 0, subDone = 0;
    try { subTotal = task.GetValue<int>( "SubtasksTotal" ); } catch { }
    try { subDone  = task.GetValue<int>( "SubtasksDone"  ); } catch { }
    if( subTotal > 0 ) subtasksStr = subDone + " / " + subTotal;

    commentCnt = GetCommentCount( task );
    attachCnt  = GetAttachmentCount( task );

    string prioRu = priorityKey;
    if     ( priorityKey == "urgent" ) prioRu = "Сверхсрочный";
    else if( priorityKey == "high"   ) prioRu = "Высокий";
    else if( priorityKey == "medium" ) prioRu = "Средний";
    else if( priorityKey == "low"    ) prioRu = "Низкий";

    string statusRu = (statusIdx >= 0 && statusIdx < 4) ? colNames[statusIdx] : "?";

    return new string[] {
        statusRu,
        title,
        details,
        assigneeName,
        creatorName,
        prioRu,
        dueDateStr,
        createdStr,
        completedStr,
        overdueStr,
        tagsStr,
        subtasksStr,
        commentCnt.ToString(),
        attachCnt.ToString()
    };
}

// Excel COM wrapper + enums (взято из T974, переименовано с префиксом Kb)
public enum KbXlAlignHorizontal
{
    xlGeneral = 1,
    xlLeft = -4131,
    xlCenter = -4108,
    xlRight = -4152,
    xlFill = 5,
    xlJustify = -4130,
    xlCenterAcrossSelection = 7,
    xlDistributed = -4117
}

public enum KbXlAlignVertical
{
    xlTop = 1,
    xlCenter = -4108,
    xlBottom = -4107,
    xlJustify = -4130,
    xlDistributed = -4117
}

public enum KbXlPageOrientation
{
    xlPortrait = 1,
    xlLandscape = 2
}

public enum KbXlBordersIndex
{
    xlDiagonalDown = 5,
    xlDiagonalUp = 6,
    xlEdgeBottom = 9,
    xlEdgeLeft = 7,
    xlEdgeRight = 10,
    xlEdgeTop = 8,
    xlInsideHorizontal = 12,
    xlInsideVertical = 11
}

public enum KbXlLineStyle
{
    xlContinuous = 1,
    xlDash = -4115,
    xlDashDot = 4,
    xlDashDotDot = 5,
    xlDot = -4118,
    xlDouble = -4119,
    xlLineStyleNone = -4142,
    xlSlantDashDot = 13
}

public enum KbXlBorderWeight
{
    xlHairline = 1,
    xlMedium = -4138,
    xlThick = 4,
    xlThin = 2
}

public class KbExcel : IDisposable
{
    object FExcel = null;
    object FWorkbooks, FWorkbook, FWorksheets, FWorksheet, FRange;

    public KbExcel()
    {
        FExcel = Activator.CreateInstance( Type.GetTypeFromProgID( "Excel.Application" ) );
    }

    public void Dispose()
    {
        try
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject( FWorkbooks );
            System.Runtime.InteropServices.Marshal.ReleaseComObject( FWorkbook );
            System.Runtime.InteropServices.Marshal.ReleaseComObject( FWorksheets );
            System.Runtime.InteropServices.Marshal.ReleaseComObject( FWorksheet );
            System.Runtime.InteropServices.Marshal.ReleaseComObject( FRange );
            System.Runtime.InteropServices.Marshal.ReleaseComObject( FExcel );
        }
        catch { }
        finally
        {
            FWorkbooks = null; FWorkbook = null; FWorksheets = null;
            FWorksheet = null; FRange = null;    FExcel = null;
        }
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
    }

    public void Kill()
    {
        int id;
        var Hwnd = (int) FExcel.GetType().InvokeMember(
            "Hwnd", System.Reflection.BindingFlags.GetProperty, null, FExcel, null );
        GetWindowThreadProcessId( Hwnd, out id );
        System.Diagnostics.Process.GetProcessById( id ).Kill();
    }

    public bool Visible
    {
        get
        {
            return Convert.ToBoolean( FExcel.GetType().InvokeMember(
                "Visible", System.Reflection.BindingFlags.GetProperty, null, FExcel, null ) );
        }
        set
        {
            FExcel.GetType().InvokeMember(
                "Visible", System.Reflection.BindingFlags.SetProperty, null, FExcel, new object[] { value } );
        }
    }

    public void CreateBook()
    {
        FWorkbooks = FExcel.GetType().InvokeMember(
            "Workbooks", System.Reflection.BindingFlags.GetProperty, null, FExcel, null );
        FWorkbook = FWorkbooks.GetType().InvokeMember(
            "Add", System.Reflection.BindingFlags.InvokeMethod, null, FWorkbooks, null );
        FWorksheets = FWorkbook.GetType().InvokeMember(
            "Worksheets", System.Reflection.BindingFlags.GetProperty, null, FWorkbook, null );
        FWorksheet = FWorksheets.GetType().InvokeMember(
            "Item", System.Reflection.BindingFlags.GetProperty, null, FWorksheets, new object[] { 1 } );
    }

    public void OpenWorksheet( int AIndex )
    {
        FWorksheet = FWorksheets.GetType().InvokeMember(
            "Item", System.Reflection.BindingFlags.GetProperty, null, FWorksheets, new object[] { AIndex } );
    }

    public void CloseBook()
    {
        FWorkbook.GetType().InvokeMember(
            "Close", System.Reflection.BindingFlags.InvokeMethod, null, FWorkbook, new object[] { false } );
    }

    public void Quit()
    {
        FExcel.GetType().InvokeMember(
            "Quit", System.Reflection.BindingFlags.InvokeMethod, null, FExcel, null );
    }

    public void SetValue( string AAddress, string AValue, string AFormat )
    {
        FRange = FWorksheet.GetType().InvokeMember(
            "Range", System.Reflection.BindingFlags.GetProperty, null, FWorksheet, new object[] { AAddress } );
        FRange.GetType().InvokeMember(
            "NumberFormat", System.Reflection.BindingFlags.SetProperty, null, FRange, new object[] { AFormat } );
        FRange.GetType().InvokeMember(
            "Value", System.Reflection.BindingFlags.SetProperty, null, FRange, new object[] { AValue } );
    }

    public void AlignHorizontal( string AAddress, KbXlAlignHorizontal AAlign )
    {
        FRange = FWorksheet.GetType().InvokeMember(
            "Range", System.Reflection.BindingFlags.GetProperty, null, FWorksheet, new object[] { AAddress } );
        FRange.GetType().InvokeMember(
            "HorizontalAlignment", System.Reflection.BindingFlags.SetProperty, null, FRange, new object[] { AAlign } );
    }

    public void AlignVertical( string AAddress, KbXlAlignVertical AAlign )
    {
        FRange = FWorksheet.GetType().InvokeMember(
            "Range", System.Reflection.BindingFlags.GetProperty, null, FWorksheet, new object[] { AAddress } );
        FRange.GetType().InvokeMember(
            "VerticalAlignment", System.Reflection.BindingFlags.SetProperty, null, FRange, new object[] { AAlign } );
    }

    public void SetWrapText( string AAddress )
    {
        FRange = FWorksheet.GetType().InvokeMember(
            "Range", System.Reflection.BindingFlags.GetProperty, null, FWorksheet, new object[] { AAddress } );
        FRange.GetType().InvokeMember(
            "WrapText", System.Reflection.BindingFlags.SetProperty, null, FRange, new object[] { true } );
    }

    public void SetFont( string AAddress, System.Drawing.Font AFont )
    {
        FRange = FWorksheet.GetType().InvokeMember(
            "Range", System.Reflection.BindingFlags.GetProperty, null, FWorksheet, new object[] { AAddress } );
        object f = FRange.GetType().InvokeMember(
            "Font", System.Reflection.BindingFlags.GetProperty, null, FRange, null );
        f.GetType().InvokeMember( "Size", System.Reflection.BindingFlags.SetProperty, null, f, new object[] { AFont.Size } );
        f.GetType().InvokeMember( "Bold", System.Reflection.BindingFlags.SetProperty, null, f, new object[] { AFont.Bold } );
        f.GetType().InvokeMember( "Name", System.Reflection.BindingFlags.SetProperty, null, f, new object[] { AFont.Name } );
    }

    public void SetColor( string AAddress, int AColorIndex, int APatternIndex )
    {
        FRange = FWorksheet.GetType().InvokeMember(
            "Range", System.Reflection.BindingFlags.GetProperty, null, FWorksheet, new object[] { AAddress } );
        object i = FRange.GetType().InvokeMember(
            "Interior", System.Reflection.BindingFlags.GetProperty, null, FRange, null );
        i.GetType().InvokeMember( "ColorIndex", System.Reflection.BindingFlags.SetProperty, null, i, new object[] { AColorIndex } );
        i.GetType().InvokeMember( "Pattern",    System.Reflection.BindingFlags.SetProperty, null, i, new object[] { APatternIndex } );
    }

    public void SetRangeBorders( string AAddress, KbXlLineStyle ALineStyle, KbXlBorderWeight AWeight )
    {
        FRange = FWorksheet.GetType().InvokeMember(
            "Range", System.Reflection.BindingFlags.GetProperty, null, FWorksheet, new object[] { AAddress } );
        object borders = FRange.GetType().InvokeMember(
            "Borders", System.Reflection.BindingFlags.GetProperty, null, FRange, new object[] { } );
        borders.GetType().InvokeMember( "LineStyle", System.Reflection.BindingFlags.SetProperty, null, borders, new object[] { ALineStyle } );
        borders.GetType().InvokeMember( "Weight",    System.Reflection.BindingFlags.SetProperty, null, borders, new object[] { AWeight } );
    }

    public void BorderAround( string AAddress, KbXlLineStyle ALineStyle, KbXlBorderWeight AWeight )
    {
        FRange = FWorksheet.GetType().InvokeMember(
            "Range", System.Reflection.BindingFlags.GetProperty, null, FWorksheet, new object[] { AAddress } );
        FRange.GetType().InvokeMember(
            "BorderAround", System.Reflection.BindingFlags.InvokeMethod, null, FRange, new object[] { ALineStyle, AWeight, 0, null } );
    }

    public void AutoFit( string AAddress )
    {
        FRange = FWorksheet.GetType().InvokeMember(
            "Range", System.Reflection.BindingFlags.GetProperty, null, FWorksheet, new object[] { AAddress } );
        object cols = FRange.GetType().InvokeMember(
            "Columns", System.Reflection.BindingFlags.GetProperty, null, FRange, null );
        cols.GetType().InvokeMember(
            "AutoFit", System.Reflection.BindingFlags.InvokeMethod, null, cols, null );
    }

    public static string GetColNameFromIndex( int columnNumber )
    {
        int dividend = columnNumber;
        string columnName = String.Empty;
        int modulo;
        while( dividend > 0 )
        {
            modulo = (dividend - 1) % 26;
            columnName = Convert.ToChar( 65 + modulo ).ToString() + columnName;
            dividend = (int)( ( dividend - modulo ) / 26 );
        }
        return columnName;
    }

    // Восстановить окно Excel (если оно свёрнуто Soyuz-клиентом) и вывести его на передний план.
    // Excel WindowState constants: xlNormal = -4143, xlMaximized = -4137, xlMinimized = -4140.
    // ShowWindow SW_RESTORE = 9.
    public void BringToFront()
    {
        try
        {
            FExcel.GetType().InvokeMember(
                "WindowState", System.Reflection.BindingFlags.SetProperty, null, FExcel, new object[] { -4143 } );
        }
        catch { }
        try
        {
            var hwndInt = (int) FExcel.GetType().InvokeMember(
                "Hwnd", System.Reflection.BindingFlags.GetProperty, null, FExcel, null );
            var hwnd = new IntPtr( hwndInt );
            ShowWindow( hwnd, 9 );      // SW_RESTORE – снимает минимизацию
            SetForegroundWindow( hwnd ); // выводит окно поверх остальных
        }
        catch { }
    }
}

[System.Runtime.InteropServices.DllImport("user32.dll")]
static extern int GetWindowThreadProcessId( int hWnd, out int lpdwProcessId );

[System.Runtime.InteropServices.DllImport("user32.dll")]
static extern bool SetForegroundWindow( IntPtr hWnd );

[System.Runtime.InteropServices.DllImport("user32.dll")]
static extern bool ShowWindow( IntPtr hWnd, int nCmdShow );

