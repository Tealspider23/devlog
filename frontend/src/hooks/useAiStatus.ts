import { useQuery } from '@tanstack/react-query'
import { getAiStatus } from '../api/ai'
import { qk } from '../lib/queryKeys'

/**
 * `/v1/ai/status` runs a live provider probe (up to two endpoints, 10s
 * connect timeout each), so it does not get the global 30s staleTime — a
 * cheap-read assumption would turn every focus/remount into a network probe.
 * Used on Chat and Settings, and prefetched once at App mount so Chat's
 * picker is warm without Chat's input ever waiting on it.
 */
export function useAiStatus() {
  return useQuery({
    queryKey: qk.aiStatus(),
    queryFn: ({ signal }) => getAiStatus(signal),
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  })
}
