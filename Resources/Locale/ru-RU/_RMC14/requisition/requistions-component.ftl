# Компьютер снабжения
requisition-paperwork-receiver-name = Логистический отдел
requisition-paperwork-reward-message = Подтверждение получено! Переведено ${$amount} из излишка бюджета.

# Накладная на поставку
requisition-paper-print-name = Накладная: {$name}
requisition-paper-print-manifest = [head=2]
    {$containerName}[/head][bold]{$content}[/bold][head=2]
    ВЕС: {$weight} фунтов
    ЛОТ: {$lot}
    С/Н: {$serialNumber}[/head]
requisition-paper-print-content = - {$count} {$item}