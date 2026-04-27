import { useEffect, useState } from 'react'
import {
  Box,
  Button,
  Chip,
  CircularProgress,
  Divider,
  Paper,
  Stack,
  Typography,
} from '@mui/material'
import type { PlanDto, SubscriptionStatusDto } from '../types/chat'
import { billingService } from '../services/billingService'

interface BillingPageProps {
  onBack: () => void
}

export function BillingPage({ onBack }: BillingPageProps) {
  const [plans, setPlans] = useState<PlanDto[]>([])
  const [subscription, setSubscription] = useState<SubscriptionStatusDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [subscribing, setSubscribing] = useState<string | null>(null)
  const [cancelling, setCancelling] = useState(false)

  useEffect(() => {
    Promise.all([billingService.getPlans(), billingService.getSubscription()]).then(
      ([p, s]) => {
        setPlans(p)
        setSubscription(s)
        setLoading(false)
      },
    )
  }, [])

  async function handleSubscribe(planId: string) {
    setSubscribing(planId)
    try {
      const { checkoutUrl } = await billingService.subscribe(planId)
      window.location.href = checkoutUrl
    } finally {
      setSubscribing(null)
    }
  }

  async function handleCancel() {
    setCancelling(true)
    try {
      await billingService.cancelSubscription()
      setSubscription(await billingService.getSubscription())
    } finally {
      setCancelling(false)
    }
  }

  if (loading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100vh' }}>
        <CircularProgress />
      </Box>
    )
  }

  return (
    <Box sx={{ maxWidth: 900, mx: 'auto', px: 3, py: 4 }}>
      <Button variant="text" onClick={onBack} sx={{ mb: 3 }}>
        ← Back to Chat
      </Button>

      <Typography variant="h4" sx={{ fontWeight: 500, mb: 1 }}>
        Manage Plan
      </Typography>

      {subscription && (
        <Paper variant="outlined" sx={{ p: 2, mb: 4, display: 'flex', alignItems: 'center', gap: 2 }}>
          <Typography variant="body1" fontWeight={500}>
            Current plan:
          </Typography>
          <Typography variant="body1">{subscription.planName}</Typography>
          <Chip
            label={subscription.status}
            size="small"
            color={subscription.status === 'Active' ? 'success' : 'default'}
          />
          {subscription.status === 'Active' && (
            <Button
              size="small"
              variant="outlined"
              color="error"
              onClick={handleCancel}
              disabled={cancelling}
              sx={{ ml: 'auto' }}
            >
              {cancelling ? 'Cancelling…' : 'Cancel subscription'}
            </Button>
          )}
        </Paper>
      )}

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={3}>
        {plans
          .filter((p) => p.tier !== 'Free')
          .map((plan) => (
            <Paper
              key={plan.id}
              elevation={2}
              sx={{ flex: 1, p: 3, display: 'flex', flexDirection: 'column', gap: 2 }}
            >
              <Typography variant="h5" fontWeight={600}>
                {plan.name}
              </Typography>
              <Typography variant="h6" color="text.secondary">
                ${plan.pricePerMonth} / month
              </Typography>
              <Divider />
              <Stack spacing={0.5} sx={{ flex: 1 }}>
                {plan.features.map((f) => (
                  <Typography key={f} variant="body2">
                    ✓ {f}
                  </Typography>
                ))}
              </Stack>
              <Button
                variant="contained"
                disabled={
                  subscribing === plan.id ||
                  subscription?.planName === plan.name
                }
                onClick={() => handleSubscribe(plan.id)}
              >
                {subscription?.planName === plan.name ? 'Current' : 'Subscribe'}
              </Button>
            </Paper>
          ))}
      </Stack>

      {plans.some((p) => p.tier === 'Free') && (
        <Box sx={{ mt: 4 }}>
          <Typography variant="h6" gutterBottom>
            Free
          </Typography>
          <Typography variant="body2" color="text.secondary">
            $0 / month
          </Typography>
          <Stack spacing={0.5} sx={{ mt: 1 }}>
            {plans
              .find((p) => p.tier === 'Free')!
              .features.map((f) => (
                <Typography key={f} variant="body2">
                  ✓ {f}
                </Typography>
              ))}
          </Stack>
        </Box>
      )}
    </Box>
  )
}
