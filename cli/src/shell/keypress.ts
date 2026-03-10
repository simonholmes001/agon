import { emitKeypressEvents } from 'node:readline';

export function createKeypressInitializer(
  stream: NodeJS.ReadableStream,
  emit: (stream: NodeJS.ReadableStream) => void = emitKeypressEvents
): () => void {
  let initialized = false;

  return () => {
    if (initialized) {
      return;
    }

    emit(stream);
    initialized = true;
  };
}
