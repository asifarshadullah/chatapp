import type { ChatMessage } from '../types/chat'

interface Props {
  message: ChatMessage
}

export function MessageBubble({ message }: Props) {
  return <div className={`bubble--${message.role}`}>{message.message}</div>
}
