// Функция: уведомление исполнителю при назначении задачи другим человеком.
// Используется WorkLoad-тип «Всплывающее сообщение» (Exclamation).
// Формат уведомления: «Новая задача «TaskName» от Иванова С.А.»
//
// Требования в PLM:
//   WorkLoad-шаблон: WorkLoads\BASIC\Message\ExclamationKanban
//   Атрибуты KanbanTask: Assignee (Reference → User), TaskName (String)
// ═══════════════════════════════════════════════════════════════════════

private const string MSG_TEMPLATE_PATH = @"WorkLoads\BASIC\Message\ExclamationKanban";

public override void OnBeforeSave( InfoObject obj )
{
    try
    {
        CheckAssigneeChanged( obj );
    }
    catch( Exception ex )
    {
        // Не прерываем сохранение из-за ошибки уведомления
        Service.HandleException( ex, "KanbanTask.OnBeforeSave: " + ex.Message );
    }
}

// ─── Проверка смены исполнителя ──────────────────────────────────────
private void CheckAssigneeChanged( InfoObject obj )
{
    // Получаем нового исполнителя рекомендованным способом (не через .Value.GetValue)
    User newAssignee = null;
    try { newAssignee = obj.GetUser( "Assignee" ); }
    catch { return; }
    if( newAssignee == null ) return;

    // Сравниваем с сохранённым значением (защита от повторных уведомлений).
    // PersistedValue — низкоуровневый API, используем только здесь.
    // PersistedValue == null → объект новый (никогда не сохранялся) → продолжаем.
    try
    {
        var assigneeAttr = obj.GetAttribute( "Assignee" );
        if( assigneeAttr != null )
        {
            var persisted = assigneeAttr.PersistedValue;
            if( persisted != null )
            {
                var oldAssignee = persisted.GetValue<User>();
                if( oldAssignee != null && oldAssignee.Id == newAssignee.Id ) return;
            }
        }
    }
    catch { /* не можем прочитать старое значение — считаем изменённым */ }

    // Не уведомляем, если назначаешь самому себе
    var currentUser = Service.GetCurrentUser();
    if( currentUser != null && newAssignee.Id == currentUser.Id ) return;

    var msgTemplate = Service.GetTemplate( MSG_TEMPLATE_PATH );
    if( msgTemplate == null ) return;

    var taskName   = obj.GetString( "TaskName" ) ?? obj.ToString();
    var senderName = FormatSenderName( currentUser );

    // Собираем информативную строку: только приоритет
    var extra = "";
    var prio = ( obj.GetString( "Priority" ) ?? "" ).Trim();
    if( prio == "urgent" )      extra += " | \u26a0 \u0421\u0440\u043e\u0447\u043d\u0430\u044f";
    else if( prio == "high" )   extra += " | \u0412\u044b\u0441\u043e\u043a\u0438\u0439 \u043f\u0440\u0438\u043e\u0440\u0438\u0442\u0435\u0442";

    // Сбросить список «видевших» — новый исполнитель увидит бейдж «НОВАЯ»
    try { obj["SeenByList"] = ""; } catch { }

    var w = new WorkItem( msgTemplate, newAssignee );
    w[ "Subject" ] = string.IsNullOrEmpty( senderName )
        ? "\u041d\u043e\u0432\u0430\u044f \u0437\u0430\u0434\u0430\u0447\u0430: " + taskName + extra
        : "\u041d\u043e\u0432\u0430\u044f \u0437\u0430\u0434\u0430\u0447\u0430 \u00ab" + taskName + "\u00bb \u043e\u0442 " + senderName + extra;

    // Гасим штатное PLM/Windows-оповещение до отправки: наше окно покажет ExclamationKanban.OnUpdated.
    try { w[ "SilentMode" ] = true; } catch { }
    w.Params = obj.NameKey;
    try { w.MarkAsViewedBy( newAssignee ); } catch { }
    w.StatusOperation = WorkItemBase.StatusEnum.Sent;

    // Повторная отметка закрывает случаи, когда PLM пересобрал состояние при переводе в Sent.
    try { w.MarkAsViewedBy( newAssignee ); } catch { }
}

// ─── Форматирование имени отправителя: «Иванова С.А.» ────────────────
// Ключи атрибутов User: GivenName=Фамилия, FirstName=Имя, SecondName=Отчество
private string FormatSenderName( User user )
{
    if( user == null ) return "";

    var surname    = ( user.GetString( "GivenName"  ) ?? "" ).Trim();
    var firstName  = ( user.GetString( "FirstName"  ) ?? "" ).Trim();
    var patronymic = ( user.GetString( "SecondName" ) ?? "" ).Trim();

    var genitSurname = DeclineSurnameGenitive( surname );

    var initials = new System.Text.StringBuilder();
    if( firstName.Length  > 0 ) initials.Append( char.ToUpper( firstName[0]  ) ).Append( '.' );
    if( patronymic.Length > 0 ) initials.Append( char.ToUpper( patronymic[0] ) ).Append( '.' );

    if( genitSurname.Length == 0 ) return initials.ToString();
    if( initials.Length     == 0 ) return genitSurname;
    return genitSurname + " " + initials;
}

// ─── Склонение фамилии в родительный падеж ───────────────────────────
// Покрывает типичные русские фамилии. Порядок проверок важен: более
// конкретные окончания проверяются раньше.
//
// Примеры:
//   Иванов    → Иванова      Иванова    → Ивановой
//   Петров    → Петрова      Тимошина   → Тимошиной
//   Путин     → Путина       Горская    → Горской
//   Дубровский→ Дубровского  Ковалевская→ Ковалевской
//   Толстой   → Толстого     Молодая    → Молодой
//   Горький   → Горького     Бойко      → Бойко
//   Москвита  → Москвиты     Черненко   → Черненко
//   Купцова   → Купцовой
private string DeclineSurnameGenitive( string s )
{
    if( string.IsNullOrEmpty( s ) || s.Length < 2 ) return s ?? "";
    var lo = s.ToLowerInvariant();

    // ── Прилагательные мужские ──────────────────────────────────────
    // -ский/-цкий и под. → -ского/-цкого (Дубровский→Дубровского)
    if( lo.EndsWith( "ский" ) || lo.EndsWith( "цкий" ) )
        return s.Substring( 0, s.Length - 2 ) + "ого";

    // -ий/-ый → -ого (Горький→Горького, Чёрный→Чёрного)
    if( lo.EndsWith( "ий" ) || lo.EndsWith( "ый" ) )
        return s.Substring( 0, s.Length - 2 ) + "ого";

    // ── Прилагательные женские ──────────────────────────────────────
    // -ская/-цкая → -ской (Горская→Горской, Ковалевская→Ковалевской)
    if( lo.EndsWith( "ская" ) || lo.EndsWith( "цкая" ) )
        return s.Substring( 0, s.Length - 2 ) + "ой";

    // -ая → -ой (Молодая→Молодой)
    if( lo.EndsWith( "ая" ) )
        return s.Substring( 0, s.Length - 2 ) + "ой";

    // ── Мужской род, окончание -ой ──────────────────────────────────
    // -ой → -ого (Толстой→Толстого)
    if( lo.EndsWith( "ой" ) )
        return s.Substring( 0, s.Length - 2 ) + "ого";

    // ── Женский род на -а ───────────────────────────────────────────
    // -ова/-ева/-ёва → -овой/-евой (Иванова→Ивановой, Купцова→Купцовой)
    if( lo.EndsWith( "ова" ) || lo.EndsWith( "ева" ) || lo.EndsWith( "ёва" ) )
        return s.Substring( 0, s.Length - 1 ) + "ой";

    // -ина/-ына → -иной/-ыной (Тимошина→Тимошиной)
    if( lo.EndsWith( "ина" ) || lo.EndsWith( "ына" ) )
        return s.Substring( 0, s.Length - 1 ) + "ой";

    // ── Мужской род ─────────────────────────────────────────────────
    // -ов/-ев/-ёв → +а (Иванов→Иванова, Медведев→Медведева)
    if( lo.EndsWith( "ов" ) || lo.EndsWith( "ев" ) || lo.EndsWith( "ёв" ) )
        return s + "а";

    // -ин/-ын → +а (Путин→Путина, Ельцин→Ельцина)
    if( lo.EndsWith( "ин" ) || lo.EndsWith( "ын" ) )
        return s + "а";

    // ── Неизменяемые ────────────────────────────────────────────────
    // -о/-е/-э → без изменений (Бойко, Черненко, Шевченко)
    if( lo.EndsWith( "о" ) || lo.EndsWith( "е" ) || lo.EndsWith( "э" ) )
        return s;

    // ── Первое склонение на -а ──────────────────────────────────────
    // -а → -ы (Москвита→Москвиты, Лысагора→Лысагоры)
    if( lo.EndsWith( "а" ) )
        return s.Substring( 0, s.Length - 1 ) + "ы";

    // ── Остальные (согласный, иностранные) ─ без изменений ──────────
    return s;
}
