using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json.Linq;
using SeaBattle.Shared.Models;

namespace SeaBattle.Client
{
    public partial class GamePage
    {
        private void AddChatLine(string line)
        {
            ChatListBox.Items.Add(line);
            if (ChatListBox.Items.Count > 0)
                ChatListBox.ScrollIntoView(ChatListBox.Items[ChatListBox.Items.Count - 1]);
        }

        private async void ChatSendButton_Click(object sender, RoutedEventArgs e)
        {
            await SendChatMessageAsync();
        }

        private void ChatInputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = SendChatMessageAsync();
            }
        }

        private async Task SendChatMessageAsync()
        {
            string text = ChatInputBox?.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                ChatInputBox.Text = "";
                AddChatLine($"Вы: {text}");
                var message = new NetworkMessage
                {
                    Type = MessageType.ChatMessage,
                    SenderId = App.PlayerId,
                    Data = JObject.FromObject(new ChatMessageData
                    {
                        Message = text,
                        SenderName = App.PlayerName
                    })
                };
                await SendMessageAsync(message);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка отправки: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
