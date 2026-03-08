/**
 * Data Formatters
 * 
 * Utilities for formatting dates, durations, IDs, and other data types.
 */

/**
 * Format a date to relative time (e.g., "2 hours ago", "just now")
 */
export function formatDate(date: Date | string): string {
  const dateObj = typeof date === 'string' ? new Date(date) : date;
  const now = new Date();
  const diffMs = now.getTime() - dateObj.getTime();
  const diffSeconds = Math.floor(diffMs / 1000);
  
  if (diffSeconds < 60) {
    return 'just now';
  }
  
  const diffMinutes = Math.floor(diffSeconds / 60);
  if (diffMinutes < 60) {
    return `${diffMinutes} minute${diffMinutes === 1 ? '' : 's'} ago`;
  }
  
  const diffHours = Math.floor(diffMinutes / 60);
  if (diffHours < 24) {
    return `${diffHours} hour${diffHours === 1 ? '' : 's'} ago`;
  }
  
  const diffDays = Math.floor(diffHours / 24);
  return `${diffDays} day${diffDays === 1 ? '' : 's'} ago`;
}

/**
 * Format duration in seconds to human-readable format (e.g., "1h 23m 45s")
 */
export function formatDuration(seconds: number): string {
  if (seconds === 0) {
    return '0s';
  }
  
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const secs = seconds % 60;
  
  const parts: string[] = [];
  if (hours > 0) {
    parts.push(`${hours}h`);
  }
  if (minutes > 0 || hours > 0) {
    parts.push(`${minutes}m`);
  }
  parts.push(`${secs}s`);
  
  return parts.join(' ');
}

/**
 * Shorten a session ID (UUID) to first 8 characters
 */
export function formatSessionId(id: string): string {
  if (id.length <= 8) {
    return id;
  }
  return id.slice(0, 8);
}

/**
 * Truncate text to max length and add ellipsis
 */
export function truncateText(text: string, maxLength: number): string {
  if (text.length <= maxLength) {
    return text;
  }
  return text.slice(0, maxLength) + '…';
}

/**
 * Format convergence score as percentage (e.g., 0.756 -> "75.6%")
 */
export function formatConvergence(score: number): string {
  return `${(score * 100).toFixed(1)}%`;
}
