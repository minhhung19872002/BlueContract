import { useQuery } from '@tanstack/react-query';
import { apiClient } from './http';
import type { components } from './schema';

export type SystemInfo = components['schemas']['SystemInfoResponse'];

export interface HealthReport {
  status: 'Healthy' | 'Degraded' | 'Unhealthy';
  durationMs: number;
  checks: Record<string, { status: string; description?: string | null; durationMs: number }>;
}

export function useSystemInfo() {
  return useQuery({
    queryKey: ['system', 'info'],
    queryFn: async () => (await apiClient.get<SystemInfo>('/api/system/info')).data,
  });
}

export function useReadiness() {
  return useQuery({
    queryKey: ['system', 'ready'],
    // 503 still carries a JSON report; accept it so the page can show which dependency is down.
    queryFn: async () =>
      (await apiClient.get<HealthReport>('/health/ready', { validateStatus: (s) => s === 200 || s === 503 }))
        .data,
    refetchInterval: 15_000,
  });
}
