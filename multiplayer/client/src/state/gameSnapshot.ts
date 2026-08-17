import type { GameSnapshot } from '../contracts/rooms';

export function shouldAcceptGameSnapshot(
  current: GameSnapshot | null,
  next: GameSnapshot,
  roomId: string,
): boolean {
  return next.roomId === roomId && (current === null || next.revision >= current.revision);
}
