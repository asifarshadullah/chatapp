import { useEffect, useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Paper,
  Stack,
  Typography,
} from '@mui/material'
import type { CheckoutMode, PlanDto, SubscriptionStatusDto } from '../types/chat'
import { billingService, UpgradesUnavailableError } from '../services/billingService'

const UPGRADE_FAILED_MESSAGE = 'The upgrade could not be started. Please try again later.'
const LOAD_FAILED_MESSAGE = 'Manage Plan could not be loaded. Please try again later.'

// Stripe confirms a payment by webhook, which can arrive a little after the user is back.
const CONFIRM_CHECK_INTERVAL_MS = 3_000
const CONFIRM_CHECKS = 10

interface BillingPageProps {
  onBack: () => void
  /** Set when the app was opened by Stripe's return after a checkout. */
  returnedFrom?: 'success' | 'cancel'
}

interface Loaded {
  plans: PlanDto[]
  subscription: SubscriptionStatusDto
  mode: CheckoutMode
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' })
}

export function BillingPage({ onBack, returnedFrom }: BillingPageProps) {
  const [loaded, setLoaded] = useState<Loaded | null>(null)
  const [loadFailed, setLoadFailed] = useState(false)
  const [subscribing, setSubscribing] = useState<string | null>(null)
  const [cancelling, setCancelling] = useState(false)
  const [subscribeError, setSubscribeError] = useState<string | null>(null)
  const [confirming, setConfirming] = useState<'waiting' | 'late' | null>(returnedFrom === 'success' ? 'waiting' : null)

  useEffect(() => {
    let cancelled = false
    Promise.all([billingService.getPlans(), billingService.getSubscription(), billingService.getCheckoutMode()])
      .then(([plans, subscription, mode]) => {
        if (cancelled) return
        setLoaded({ plans, subscription, mode })
        if (subscription.tier !== 'Free') setConfirming(null)
      })
      .catch(() => {
        if (!cancelled) setLoadFailed(true)
      })
    return () => {
      cancelled = true
    }
  }, [])

  const isWaiting = confirming === 'waiting' && loaded !== null
  useEffect(() => {
    if (!isWaiting) return
    let checks = 0
    let stopped = false
    const timer = setInterval(async () => {
      checks += 1
      // Stopped here rather than by the effect's cleanup, which waits for the next render.
      const last = checks >= CONFIRM_CHECKS
      if (last) clearInterval(timer)
      const subscription = await billingService.getSubscription().catch(() => null)
      if (stopped) return
      if (subscription) setLoaded((current) => (current ? { ...current, subscription } : current))
      if (subscription && subscription.tier !== 'Free') {
        clearInterval(timer)
        setConfirming(null)
      } else if (last) {
        setConfirming('late')
      }
    }, CONFIRM_CHECK_INTERVAL_MS)
    return () => {
      stopped = true
      clearInterval(timer)
    }
  }, [isWaiting])

  async function handleSubscribe(planId: string) {
    setSubscribing(planId)
    setSubscribeError(null)
    try {
      const { checkoutUrl } = await billingService.subscribe(planId)
      window.location.href = checkoutUrl
    } catch (err) {
      setSubscribeError(err instanceof UpgradesUnavailableError ? err.message : UPGRADE_FAILED_MESSAGE)
    } finally {
      setSubscribing(null)
    }
  }

  async function handleCancel() {
    setCancelling(true)
    try {
      await billingService.cancelSubscription()
      const subscription = await billingService.getSubscription()
      setLoaded((current) => (current ? { ...current, subscription } : current))
    } finally {
      setCancelling(false)
    }
  }

  const backButton = (
    <Button variant="text" onClick={onBack} sx={{ mb: 3 }}>
      ← Back to Chat
    </Button>
  )

  if (loadFailed) {
    return (
      <Box sx={{ maxWidth: 900, mx: 'auto', px: 3, py: 4 }}>
        {backButton}
        <Alert severity="error">{LOAD_FAILED_MESSAGE}</Alert>
      </Box>
    )
  }

  if (!loaded) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100vh' }}>
        <CircularProgress />
      </Box>
    )
  }

  const { plans, subscription, mode } = loaded
  const onPaidPlan = subscription.tier !== 'Free' && subscription.currentPeriodEnd !== null

  return (
    <Box sx={{ maxWidth: 900, mx: 'auto', px: 3, py: 4 }}>
      {backButton}

      <Typography variant="h4" sx={{ fontWeight: 500, mb: 3 }}>
        Manage Plan
      </Typography>

      {/* Notices are status messages; only a failed upgrade is announced as an alert. */}
      <Stack spacing={2} sx={{ mb: 3 }}>
        {confirming === 'waiting' && (
          <Alert role="status" severity="info">
            Your payment was received. Your plan updates to Pro once Stripe confirms it.
          </Alert>
        )}
        {confirming === 'late' && (
          <Alert role="status" severity="warning">
            Your plan has not updated yet. Reload the page in a minute.
          </Alert>
        )}
        {returnedFrom === 'cancel' && (
          <Alert role="status" severity="info">
            Checkout was cancelled. Nothing was charged.
          </Alert>
        )}
        {mode === 'test' && (
          <Alert role="status" severity="info">
            This is a demo in Stripe's test mode. Pay with card 4242 4242 4242 4242, any future date and any
            CVC. Nothing is charged.
          </Alert>
        )}
        {mode === 'off' && (
          <Alert role="status" severity="info">
            Upgrades are turned off here.
          </Alert>
        )}
        {subscribeError && <Alert severity="warning">{subscribeError}</Alert>}
      </Stack>

      <Paper variant="outlined" sx={{ p: 2, mb: 4, display: 'flex', alignItems: 'center', gap: 2, flexWrap: 'wrap' }}>
        <Typography variant="body1" fontWeight={500}>
          Current plan: {subscription.planName}
        </Typography>
        {onPaidPlan && (
          <Typography variant="body2" color="text.secondary">
            {subscription.cancelAtPeriodEnd ? 'Ends on' : 'Renews on'} {formatDate(subscription.currentPeriodEnd!)}
          </Typography>
        )}
        {onPaidPlan && !subscription.cancelAtPeriodEnd && (
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

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={3}>
        {plans.map((plan) => {
          const isCurrent = plan.tier === subscription.tier
          return (
            <Paper
              key={plan.id}
              component="section"
              aria-label={plan.name}
              elevation={2}
              sx={{ flex: 1, p: 3, display: 'flex', flexDirection: 'column', gap: 2 }}
            >
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <Typography variant="h5" fontWeight={600}>
                  {plan.name}
                </Typography>
                {isCurrent && <Chip label="Current plan" size="small" color="primary" />}
              </Box>
              <Typography variant="h6" color="text.secondary">
                ${plan.pricePerMonth} / month
              </Typography>
              <Typography variant="body1" sx={{ flex: 1 }}>
                {plan.dailyMessageLimit} messages a day
              </Typography>
              {plan.tier !== 'Free' && !isCurrent && (
                <Button
                  variant="contained"
                  disabled={mode === 'off' || subscribing === plan.id}
                  onClick={() => handleSubscribe(plan.id)}
                >
                  Subscribe
                </Button>
              )}
            </Paper>
          )
        })}
      </Stack>
    </Box>
  )
}
